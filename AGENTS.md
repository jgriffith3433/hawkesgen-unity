# hawkesgen-unity — Hawkes Gen for Unity (Asset Store package)

Read README.md first. Hard rules (the Asset Store Submission Guidelines; a rejection costs weeks):
- Everything that ships lives under `Assets/HawkesGen/` (one root folder), in the `HawkesGen.Editor` namespace and asmdef
  (Editor only). No runtime code, no third-party code, no dependencies on other packages (the 2D Sprite package is
  optional, through the asmdef's versionDefines). Never add or change packages in a user's project from code.
- No obsolete or deprecated Unity API, no reflection into internal APIs, no compiler warnings. It must work on 2022.3+.
- Tokens only in EditorPrefs; never in assets, scenes or builds. Nothing opens a browser unless the user clicks.
- Only the user's prompts and the files they pick go to the service. No analytics.
- Keep the documentation PDF (from `Docs/HawkesGen-Documentation.html`), `Third-Party Notices.txt` and the disclosures in LISTING.md true when behavior changes.
- The window's recipes are Hawkes Gen MCP tools (`hawkesgenwebsite/src/mcp.ts`): if a tool's arguments change there,
  change `Request()` here. Run the smoke test (README) before every upload.
