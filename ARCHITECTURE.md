# .NET Serial Toolkit Architecture

## Purpose

This repository supplies reusable serial-device and diagnostics infrastructure for .NET applications. It is a
library package, not a device application framework. Product command catalogs, proprietary protocols, UI,
calibration, training, and customer configuration remain in the consuming application.

## Composition model

The toolkit follows a **modular monolith** model: one package and one deployment unit, with namespaces acting as
responsibility and change boundaries. Namespace independence does not require zero dependencies or separate
assemblies.

The expected composition is:

```text
consuming application
  -> toolkit commands / diagnostics / logging / packaging
  -> toolkit transport and serial-port adapters
  -> System.IO.Ports and operating-system facilities
```

The consuming application owns domain policy, device protocol, UI state, dependency injection, and shutdown
orchestration. Toolkit components may be composed directly; there is no requirement to wrap every class in an
additional interface. Introduce an interface when a test seam, alternate implementation, lifecycle boundary,
or API contract needs it.

## Reuse entry points

- [ADOPTION.md](ADOPTION.md) maps common project needs to package, sample, diagnostics, or architecture-gate reuse.
- `samples/DCRimbaud.SerialToolkit.Console` demonstrates command execution and JSONL evidence without hardware.
- `samples/DCRimbaud.SerialToolkit.Avalonia` demonstrates UI-side port enumeration.

Shared records, policies, and result types should have one definition. Copying them to keep namespaces
"self-contained" creates duplicate contracts and is prohibited. Related classes in one feature area may depend
on each other without losing cohesion.

## Component map

| Component | Responsibility | Relation |
|---|---|---|
| root serial contracts | settings, transport interface, real serial adapter, port enumeration | `System.IO.Ports`; Windows metadata through `System.Management` |
| `Commands` | response classification, correlation tracking, command execution | transport contract and immutable command policies |
| `Logging` | date/size rotated JSONL writing | file-system host and caller serialization policy |
| `Diagnostics` | deterministic ZIP and manifest assembly | logging/file inputs and ZIP/JSON APIs |
| `Packaging` | offline archive creation and safe verification | ZIP, JSON, SHA-256 |
| `tools/architecture` | dependency, API, and change-coupling gates | build-time tooling, not runtime code |

A component may reuse another component's stable public records. New abstractions are justified by a real
substitution or lifecycle boundary, not by a desire to make every namespace independent.

## Dependency rules

- Dependencies point toward stable contracts and lower-level infrastructure, never back into application or UI.
- Runtime library code must not depend on samples, tests, or architecture tooling.
- `System.IO.Ports`, `System.Management`, file-system, and ZIP types stay inside their adapter or component boundary.
- Public API changes are reviewed through the API baseline.
- Circular project or namespace dependencies are rejected.
- Shared data contracts are immutable where practical and define ownership explicitly.
- Exceptions crossing a boundary are translated into result types when callers need stable failure semantics.
- The toolkit does not introduce a global service locator, static logger, or ambient connection state.

## Data and concurrency ownership

- A transport caller owns command ordering; the toolkit does not silently serialize independent commands.
- `SerialCommandExecutor` owns one request/response operation, not the application's entire device session.
- Response tracking is explicit and caller-driven; background session ownership remains with the application.
- A diagnostics or offline package operation owns its temporary files and staged extraction for the duration of the call.
- Rotating JSONL writes are serialized by the caller unless a higher-level component provides a single writer loop.
- Bounded queues, cancellation, timeout, and shutdown order belong to the consuming application's session supervisor.
- Large payloads should use immutable records, `ReadOnlyMemory<byte>`, and explicit copying or ownership transfer.

## Package and extension policy

The package remains cohesive around serial-device development and diagnostics. A new namespace is accepted when:

1. it has a product-neutral responsibility;
2. it has no dependency on samples, product UI, or proprietary protocols;
3. tests cover normal, boundary, cancellation, and failure paths where applicable;
4. public behavior and ownership are documented;
5. it composes with existing components without creating a cycle;
6. it does not duplicate an existing shared contract.

Separate assemblies or NuGet packages are justified only by a real independent versioning, deployment,
permission, or dependency-isolation requirement. Otherwise, keep the code in this package and enforce clear
namespace and dependency boundaries.

## Validation

```powershell
dotnet test tests\DCRimbaud.SerialToolkit.Tests\DCRimbaud.SerialToolkit.Tests.csproj -c Release
dotnet build samples\DCRimbaud.SerialToolkit.Avalonia\DCRimbaud.SerialToolkit.Avalonia.csproj -c Release
dotnet pack src\DCRimbaud.SerialToolkit\DCRimbaud.SerialToolkit.csproj -c Release -o artifacts\nuget
```

Architecture fixtures validate both an accepted dependency graph and a rejected violation. Unit tests alone
do not prove API compatibility or dependency direction.
