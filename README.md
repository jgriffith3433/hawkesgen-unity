# Hawkes Gen for Unity

A Unity Editor extension for [Hawkes Gen](https://hawkesgen.ai). It makes sprites, animated sprite sheets (sliced, with an
AnimationClip), tileable PBR materials, skyboxes, sound effects, voice lines and music from a prompt, and edits images you
already have, without leaving the editor. It talks to the same MCP server as Claude Code, Cursor and VS Code
(`https://hawkesgen.ai/mcp`), with the same browser sign-in.

- **The package:** `Assets/HawkesGen/` (all that ships). Window > Hawkes Gen. Docs: `Assets/HawkesGen/Documentation/Hawkes Gen Documentation.pdf` (printed from `Docs/HawkesGen-Documentation.html` with Chrome headless; edit the HTML, then reprint).
- **This repo** is the Unity project it's built and uploaded from (Unity 6000.3.7f1; the package supports 2022.3+). It has the
  Asset Store Publishing Tools in `Packages/manifest.json`; dev tools (the smoke test, export, demo-scene builder) are in
  `DevTools/`, outside `Assets` (see DevTools/README.md).
- **Publishing:** see [PUBLISHING.md](PUBLISHING.md). **Store listing text:** [LISTING.md](LISTING.md).

## How it works

| File | What |
|---|---|
| `Editor/HawkesGenClient.cs` | Browser sign-in (OAuth 2.1, PKCE, dynamic client registration, a loopback listener on a free port), tokens in EditorPrefs (never in the project or a build), MCP `tools/call` over HTTPS with refresh on 401, uploads (one-time link, PUT), downloads |
| `Editor/HawkesGenWindow.cs` | The window: account and credits, the recipes (each a Hawkes Gen MCP tool), progress bars from the task's progress, results pinged in the Project window |
| `Editor/HawkesGenImporter.cs` | Results as Unity assets: Sprites; sprite sheets sliced with the 2D Sprite package's data provider (`HAWKESGEN_SPRITE_EDITOR`, set by the asmdef when the package is present) plus a looping AnimationClip; PBR maps as a Material for Built-in, URP or HDRP; panoramas as a Skybox/Panoramic Material |
| `Editor/Json.cs` | A small JSON reader and writer (no package dependency) |

## Test

`.github/workflows/check.yml` runs on every push, with no laptop step:
- **Documentation PDF:** printed from `Docs/HawkesGen-Documentation.html` with Chrome headless; on a branch, when the HTML
  is newer than the PDF, the reprint is committed to the branch.
- **Unity:** the project compiled in `unityci/editor:ubuntu-6000.3.7f1` (no errors, no warnings in `Assets/HawkesGen`),
  licensed with the repo secrets `UNITY_EMAIL` and `UNITY_PASSWORD` (a Personal seat, returned after; `UNITY_SERIAL` for
  Plus/Pro). Without them the job warns and skips.
- **Smoke test** (Actions > Check > Run workflow, "smoke"): signs in through the real loopback flow as beta Gen's test
  account (the run's own GitHub OIDC token makes the session, no secret), uploads the sample, removes its
  background and makes pixel art, against https://beta.hawkesgen.ai (a few credits). "sprite" also makes a sprite sheet
  (a GPU clip).

By hand on Linux with Unity: `bash DevTools/ci/unity.sh compile` or `smoke` (same environment variables). Or in the editor:

```bash
# copy DevTools/ into Assets/_Dev/Editor/ first
HG_COOKIE='hg_session=...' HG_SERVER=https://hawkesgen.ai Unity -batchmode -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenSmokeTest.Run -logFile -
```
