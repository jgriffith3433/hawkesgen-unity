#!/usr/bin/env bash
# The package's checks in batch mode, for .github/workflows/check.yml (inside a unityci/editor image) or any Linux box
# with Unity. Not part of the package.
#   bash DevTools/ci/unity.sh compile   the project compiles, with no errors and no warnings in Assets/HawkesGen
#   bash DevTools/ci/unity.sh smoke     the live smoke test (DevTools/HawkesGenSmokeTest.cs), signed in as a test account
# License: UNITY_EMAIL + UNITY_PASSWORD (a Personal seat, through the Unity Licensing Client inside the editor, returned
# at the end), or with UNITY_SERIAL a Plus/Pro serial. The same way Hawkes Gen's Unity builds activate
# (hawkesgen server/unity-cloudrun/entrypoint.sh). Never echoes the secrets.
# Smoke: HG_SERVER (default https://beta.hawkesgen.ai). In GitHub Actions (permissions id-token: write) the run's own OIDC
# token signs in this repo's test account on beta Gen (POST /v1/users/test-session/github): no secret. Elsewhere,
# HG_COOKIE (a session cookie) or HG_TEST_SIGNIN (that Gen's tss_ secret). HG_SPRITE=1 also makes a sprite sheet.
set -uo pipefail
MODE="${1:-compile}"
UNITY="${UNITY_BIN:-unity-editor}"
OUT="${RUNNER_TEMP:-/tmp}/hawkesgen-unity"
mkdir -p "$OUT"
LICENSE_MODE=""

licensing_client() { find "${UNITY_PATH:-/opt/unity}" -type f -name Unity.Licensing.Client 2>/dev/null | head -1; }

activate() {
  if [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
    echo "No Unity account: set the repo secrets UNITY_EMAIL and UNITY_PASSWORD"; return 1
  fi
  if [ -n "${UNITY_SERIAL:-}" ]; then
    LICENSE_MODE=serial
    "$UNITY" -batchmode -nographics -quit -logFile "$OUT/activate.log" -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" >/dev/null 2>&1
    return $?
  fi
  local client spelling code=1
  client=$(licensing_client)
  [ -n "$client" ] || { echo "The Unity Licensing Client isn't in this image"; return 1; }
  LICENSE_MODE=personal
  # unity-cli spells it --activate-ulf, GameCI --activate-all; an unknown option means the other spelling.
  for spelling in --activate-ulf --activate-all; do
    "$client" "$spelling" --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" > "$OUT/activate.log" 2>&1
    code=$?
    [ "$code" = 0 ] && return 0
    grep -Eiq 'unrecognized|unknown (option|argument)|invalid option|usage:' "$OUT/activate.log" || break
  done
  echo "Unity activation failed (exit $code): $(grep -Eiv 'password|serial|token' "$OUT/activate.log" | grep -Ei 'error|fail|denied|refus|two.?factor' | head -1 | cut -c1-200)"
  return 1
}

license_return() {
  case "$LICENSE_MODE" in
    personal) "$(licensing_client)" --return-ulf > "$OUT/return.log" 2>&1 || echo "Returning the license failed; the seat frees itself later" ;;
    serial) "$UNITY" -batchmode -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -logFile "$OUT/return.log" >/dev/null 2>&1 || true ;;
  esac
}

# The dev tools live outside Assets (they must never ship); copy them in for the run, as DevTools/README.md says.
dev_tools_in() { mkdir -p Assets/_Dev/Editor && cp DevTools/*.cs DevTools/*.asmdef Assets/_Dev/Editor/; }

compile() {
  "$UNITY" -batchmode -nographics -quit -projectPath . -logFile "$OUT/compile.log" >/dev/null 2>&1
  local code=$? errors warnings
  errors=$(grep -E 'error CS[0-9]+' "$OUT/compile.log" | sort -u)
  warnings=$(grep -E 'Assets/HawkesGen/.*warning CS[0-9]+' "$OUT/compile.log" | sort -u)
  [ -n "$errors" ] && { echo "Compile errors:"; echo "$errors" | head -40; }
  [ -n "$warnings" ] && { echo "Warnings in Assets/HawkesGen (the Asset Store guidelines allow none):"; echo "$warnings" | head -40; }
  if [ -n "$errors" ] || [ -n "$warnings" ] || [ "$code" != 0 ]; then
    [ -z "$errors$warnings" ] && { echo "Unity exited $code:"; tail -40 "$OUT/compile.log"; }
    return 1
  fi
  # A clean exit with no compile at all (a cached Library, a license that quietly failed) proves nothing: the package's
  # own assembly must show in the log.
  if ! grep -q 'HawkesGen.Editor' "$OUT/compile.log"; then
    echo "Unity exited cleanly but the log shows no HawkesGen.Editor compile:"; tail -30 "$OUT/compile.log"; return 1
  fi
  echo "Compiled HawkesGen.Editor with no errors and no warnings in Assets/HawkesGen ($(grep -c 'warning CS' "$OUT/compile.log") warnings elsewhere)"
}

test_cookie() {
  local server="$1" exp nonce sig
  exp=$(( $(date +%s) + 300 )); nonce=$(openssl rand -hex 16)
  sig=$(printf '%s' "hawkesgen-unity.$exp.$nonce" | openssl dgst -sha256 -hmac "$HG_TEST_SIGNIN" -binary | openssl base64 -A | tr '+/' '-_' | tr -d '=')
  curl -fsS -c "$OUT/jar" -H 'Content-Type: application/json' -o /dev/null \
    -d "{\"project\":\"hawkesgen-unity\",\"exp\":$exp,\"nonce\":\"$nonce\",\"sig\":\"$sig\"}" "$server/v1/users/test-session" || return 1
  awk '$6 == "hg_session" { print "hg_session=" $7 }' "$OUT/jar"
}

github_cookie() {
  local server="$1" oidc
  oidc=$(curl -fsS -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=hawkesgen-test-signin" | sed -n 's/.*"value":"\([^"]*\)".*/\1/p')
  [ -n "$oidc" ] || return 1
  echo "::add-mask::$oidc" >&2
  curl -fsS -c "$OUT/jar" -H 'Content-Type: application/json' -o /dev/null -d "{\"token\":\"$oidc\"}" "$server/v1/users/test-session/github" || return 1
  awk '$6 == "hg_session" { print "hg_session=" $7 }' "$OUT/jar"
}

smoke() {
  export HG_SERVER="${HG_SERVER:-https://beta.hawkesgen.ai}"
  if [ -z "${HG_COOKIE:-}" ]; then
    if [ -n "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" ]; then HG_COOKIE=$(github_cookie "$HG_SERVER")
    elif [ -n "${HG_TEST_SIGNIN:-}" ]; then HG_COOKIE=$(test_cookie "$HG_SERVER")
    else echo "Smoke test needs GitHub Actions (id-token: write), HG_COOKIE or HG_TEST_SIGNIN"; return 1; fi
    [ -n "$HG_COOKIE" ] || { echo "Couldn't make a test session on $HG_SERVER"; return 1; }
    echo "::add-mask::${HG_COOKIE#hg_session=}"
    export HG_COOKIE
  fi
  "$UNITY" -batchmode -nographics -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenSmokeTest.Run -logFile "$OUT/smoke.log" >/dev/null 2>&1
  local code=$?
  grep -F '[smoke]' "$OUT/smoke.log" | grep -v '^\s*$' | head -60
  return $code
}

activate || exit 1
trap license_return EXIT
dev_tools_in
case "$MODE" in
  compile) compile ;;
  smoke) compile && smoke ;;
  *) echo "usage: unity.sh compile|smoke"; exit 2 ;;
esac
