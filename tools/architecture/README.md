# DCRimbaud .NET Architecture Gates

Integrated architecture checks for .NET device applications.

## Included

- project-reference direction checks
- project-reference cycle checks
- Domain/Application/Presentation.Core I/O leak checks
- Presentation-to-Infrastructure leak checks
- xUnit reflection-based assembly architecture test template
- public API baseline test template
- Git co-change and hotspot analysis using PowerShell only

## Not included

- product assemblies, types, protocols, or device models
- proprietary UI or business rules
- product API baselines
- generated binaries or local reports

## Usage

```powershell
pwsh -NoProfile -File scripts\check_dotnet_architecture.ps1 -Root <software-root>
pwsh -NoProfile -File scripts\analyze_change_coupling.ps1 -Root <repository-root> -Since "90 days ago" -Top 30
```

The `fixtures` directory contains one valid and one invalid project graph used by CI to verify both acceptance and rejection paths.

Copy the templates from `templates\dotnet` into a test project, replace namespace and assembly
references, and create a reviewed `PublicApiBaseline.txt`.

## License

MIT. See `LICENSE`.

## Author

Xue Weisen  
Email: xueweisen96@gmail.com  
GitHub: https://github.com/DCRimbaud
