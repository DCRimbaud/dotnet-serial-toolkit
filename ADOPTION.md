# Adoption Guide

This guide is the first stop for a new .NET serial, diagnostics, or device-tool project. Prefer the package
and the existing samples; copy only the composition that matches the application.

## Decision table

| Situation | Recommended action | Why |
|---|---|---|
| New .NET application needs a serial transport and command execution | reference `DCRimbaud.SerialToolkit` | keeps transport, ACK, and logging boundaries reusable |
| Need a host test without hardware | use or copy the `ScriptedTransport` pattern from the console sample | tests the real executor and tracker without a UART |
| Need rotated structured logs | use `RotatingJsonlWriter` with one writer owner | avoids concurrent file corruption and ad-hoc log formats |
| Need a support bundle | use `DiagnosticsPackage` | deterministic manifest and duplicate-source handling |
| Need a safe offline update artifact | use `OfflinePackage` | SHA-256 manifest and path-safe staged extraction |
| Need architecture gates | copy `tools/architecture` templates into the app or run the scripts in CI | catches dependency and public API drift |
| Need product protocol or UI | implement it in the consuming application | keeps the toolkit product-neutral |

## Package integration

```powershell
dotnet add package DCRimbaud.SerialToolkit
```

For repository development, use a project reference instead:

```xml
<ProjectReference Include="..\..\src\DCRimbaud.SerialToolkit\DCRimbaud.SerialToolkit.csproj" />
```

## Common compositions

### Text command session

```text
UI or CLI
  -> application command policy
  -> SerialCommandExecutor
  -> ISerialTransport / SerialPortTransport
  -> rotating JSONL evidence
```

Keep the ACK classifier and command catalog in the application or protocol layer. The transport only moves
bytes; the executor only executes one request/response operation.

### Host test

```text
test case
  -> ScriptedTransport or replay transport
  -> SerialCommandExecutor
  -> command result assertions
```

The fake should preserve partial reads, delays, timeouts, and response ordering that matter to the real
transport. A fake that returns the whole response in one call does not test framing behavior.

### Diagnostics and support

```text
application session
  -> evidence and settings inputs
  -> DiagnosticsPackage
  -> ZIP + manifest + verification result
```

Do not collect secrets or customer data merely because a diagnostic package supports text entries. Apply
redaction and retention policy before packaging.

## Reuse checklist

1. Define the application's session owner, cancellation token, reconnect policy, and shutdown order.
2. Keep `ISerialTransport` narrow and testable; do not expose UI or product protocol types through it.
3. Use immutable policies/records for commands and results.
4. Record raw tx/rx evidence or a replayable equivalent when field diagnosis matters.
5. Add correlation/session IDs in the application log schema.
6. Test success, timeout, cancellation, unsupported response, partial reads, and disconnect paths.
7. Review public API changes through the baseline and architecture fixtures.

## What not to do

- Do not put `SerialPort` or `Window` types into application contracts.
- Do not combine timeout, cancellation, and transport failure into one ambiguous result.
- Do not use a global service locator to avoid constructor wiring.
- Do not let a logger or diagnostics writer block command ACK processing.
- Do not copy product command catalogs into the public toolkit.
- Do not treat `dotnet build` success as proof that the session lifecycle is correct.

## Reference paths

- [Architecture](ARCHITECTURE.md)
- [Console command sample](samples/DCRimbaud.SerialToolkit.Console/README.md)
- [Avalonia enumeration sample](samples/DCRimbaud.SerialToolkit.Avalonia)
- [Architecture tooling](tools/architecture/README.md)
- [Repository](https://github.com/DCRimbaud/dotnet-serial-toolkit)
