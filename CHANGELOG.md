# Changelog

All notable changes to this project are documented here.

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
