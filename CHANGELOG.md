# Changelog

All notable changes to this project are documented here.

## 1.0.2 - 2026-10-26

- Keep cursors across a restart without a startup task. The installer now
  rewrites the cursor section of the active Windows theme file, which is what
  Windows reloads during logon. Previously the theme file kept describing the
  default pointers, so a reboot left the scheme selected but the session showing
  Windows' own cursors. Only `[Control Panel\Cursors]` is touched, and the
  original section is restored by **Restore original cursors**.
- The optional sign-in re-apply is now off by default and described as a
  fallback. New `--startup` install switch; `--no-startup` remains accepted.
- Restoring the original cursors also restores the theme cursor section.

## 1.0.1 - 2026-10-26

- Keep cursors across a restart. With **Re-apply at sign-in** enabled, the
  installer adds a per-user `Run` entry that re-applies the current cursor
  selection a few seconds after sign-in, because Windows can load its own
  cursors at logon even when the custom scheme is still selected.
- New `--reapply` command-line mode and `--no-startup` install switch.
- Restoring the original cursors now removes the sign-in entry.

## 1.0.0 - 2026-10-21

- Initial release.
- Drag-and-drop `.curskin` installer with variant selection.
- GIF, PNG, AVIF/AVIS, direct SVG, and layered SVG conversion.
- 32x32 `.cur`/`.ani` output with hotspot and animation timing preservation.
- Per-user scheme registration under `HKCU`.
- Correct Windows positional scheme order for Location Select (Pin) and
  Person Select (Person), plus source `PERSON`/`PIN` cursor support.
- Optional Mixed scheme for multiple variants.
- Original cursor backup and restore.
- Self-contained AVIF and SVG helpers.
- Visual Studio 2022 solution and .NET Framework 4.8 project; `build.cmd`
  remains the dependency-free release build.
- No custom executable or window icon; the app uses the platform default.
