# Contributing

Thanks for taking a look.

## Build

Requirements: 64-bit Windows 10/11 and .NET Framework 4.x. No NuGet packages are
required.

The dependency-free release build uses the in-box compiler:

```bat
build.cmd
```

Or open `CurskinInstaller.sln` in Visual Studio 2022 with the .NET desktop
development workload / .NET Framework 4.8 targeting pack, then build or press
F5.

Build a release zip with:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1
```

## Guidelines

- Keep the app compatible with the in-box .NET Framework compiler used by
  `build.cmd`; the Visual Studio project targets .NET Framework 4.8.
- Do not add NuGet packages. Keep `build.cmd` working without Visual Studio;
  the VS project is an IDE convenience.
- Do not commit `CurskinInstaller.exe`, `bin/`, `obj/`, `dist/`, or extracted
  `avifdec.exe`/`resvg.exe`.
- Keep the embedded resources (`avifdec.exe.gz`, `resvg.exe.gz`) in the
  repository; the app needs them at build time.
- Test at least one real `.curskin` file, including an animated cursor if you
  change conversion code.
- Update `README.txt`, `README.md`, and `THIRD-PARTY-NOTICES.txt` when behavior
  or dependencies change.

## License

By contributing, you agree that your contributions are licensed under the MIT
License.
