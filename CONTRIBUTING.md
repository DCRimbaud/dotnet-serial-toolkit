# Contributing

## Scope

This repository contains generic .NET serial and diagnostics infrastructure only. Do not submit:

- product protocols, command catalogs, or device-specific training logic
- customer names, credentials, keys, certificates, or private logs
- product UI, device maps, calibration data, or proprietary protocols
- generated binaries, packages, or local artifacts

## Required changes

- Keep public APIs documented with English XML comments.
- Add or update tests for every behavior change.
- Keep implementation comments in English where the logic is non-obvious.
- Preserve path-safety, bounded memory, deterministic file encoding, and cross-process file behavior.
- Run restore, build, and tests before opening a pull request.

```powershell
dotnet restore tests\DCRimbaud.SerialToolkit.Tests\DCRimbaud.SerialToolkit.Tests.csproj
dotnet build tests\DCRimbaud.SerialToolkit.Tests\DCRimbaud.SerialToolkit.Tests.csproj -c Release --no-restore
dotnet test tests\DCRimbaud.SerialToolkit.Tests\DCRimbaud.SerialToolkit.Tests.csproj -c Release --no-build
dotnet pack src\DCRimbaud.SerialToolkit\DCRimbaud.SerialToolkit.csproj -c Release -o artifacts\nuget
```

By contributing, you confirm that you have the right to submit the code under this repository's MIT license.
