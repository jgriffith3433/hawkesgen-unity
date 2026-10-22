# Hawkes Gen for Unity

A Unity Editor extension for [Hawkes Gen](https://hawkesgen.com). It makes sprites, animated sprite sheets (sliced, with an
AnimationClip), tileable PBR materials, skyboxes, sound effects, voice lines and music from a prompt, and edits images you
already have, without leaving the editor. It talks to the same MCP server as Claude Code, Cursor and VS Code
(`https://hawkesgen.com/mcp`), with the same browser sign-in.

- **The package:** `Assets/HawkesGen/` (all that ships). Window > Hawkes Gen. Docs: `Assets/HawkesGen/Documentation/HawkesGen.md`.
- **This repo** is the Unity project it's built and uploaded from (Unity 6000.3.7f1; the package supports 2022.3+). It also
  has the dev-only smoke test (`Assets/_Dev`) and the Asset Store Publishing Tools in `Packages/manifest.json`.
- **Publishing:** see [PUBLISHING.md](PUBLISHING.md). **Store listing text:** [LISTING.md](LISTING.md).

## How it works

| File | What |
|---|---|
| `Editor/HawkesGenClient.cs` | Browser sign-in (OAuth 2.1, PKCE, dynamic client registration, a loopback listener on a free port), tokens in EditorPrefs (never in the project or a build), MCP `tools/call` over HTTPS with refresh on 401, uploads (one-time link, PUT), downloads |
| `Editor/HawkesGenWindow.cs` | The window: account and credits, the recipes (each a Hawkes Gen MCP tool), progress bars from the task's progress, results pinged in the Project window |
| `Editor/HawkesGenImporter.cs` | Results as Unity assets: Sprites; sprite sheets sliced with the 2D Sprite package's data provider (`HAWKESGEN_SPRITE_EDITOR`, set by the asmdef when the package is present) plus a looping AnimationClip; PBR maps as a Material for Built-in, URP or HDRP; panoramas as a Skybox/Panoramic Material |
| `Editor/Json.cs` | A small JSON reader and writer (no package dependency) |

## Test

A test account's session cookie approves the sign-in, and a green-screen clip feeds the sprite-sheet step:

```bash
HG_COOKIE='hg_session=...' HG_VIDEO=/path/to/clip.mp4 Unity -batchmode -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenSmokeTest.Run -logFile -
```

It signs in through the real loopback flow, uploads the sample, removes its background, makes pixel art, and turns a clip
into a sliced sheet with an AnimationClip, against hawkesgen.com (about 6 credits).
