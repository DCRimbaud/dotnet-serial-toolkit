# .NET Host Application Templates

This directory collects copy-ready documents and points to the executable
architecture templates already used by this repository.

| Template | Purpose |
|---|---|
| `architecture-brief.md` | Design and review brief for layering, ports, data flow, threads, errors, tests, and residual risks |
| `../tools/architecture/templates/dotnet/HostApp.ArchitectureTests.cs` | Reflection-based assembly dependency checks |
| `../tools/architecture/templates/dotnet/HostApp.PublicApiTests.cs` | Public API baseline gate |
| `../samples/DCRimbaud.SerialToolkit.Console` | Host-runnable command and JSONL composition |
| `../samples/DCRimbaud.SerialToolkit.Avalonia` | UI-side serial enumeration sample |

## Use

1. Copy the architecture brief into the consuming application's design folder.
2. Copy the two test templates into the application test project.
3. Generate `PublicApiBaseline.txt` after the current API has been reviewed.
4. Replace project namespaces and assembly references in the templates.
5. Keep product protocol, device catalogs, and UI policy in the consuming application.

The `architecture-brief.md` source is maintained as a bilingual engineering
template. New public code comments and public documentation remain English-only.
