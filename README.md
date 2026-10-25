# DCRimbaud .NET Serial Toolkit

Open-source .NET serial-device and diagnostics infrastructure.

## Included

- serial-port settings independent of a product protocol
- minimal `ISerialTransport` abstraction for real ports and host fakes
- `SerialPortTransport` adapter
- Windows serial-device enumeration
- CP210x, CH340, FTDI, and PL2303 VID/PID classification
- cross-platform fallback to `SerialPort.GetPortNames()`
- UTF-8 no-BOM JSONL writer with date/size rotation and retention
- diagnostics ZIP with stable manifest, source deduplication, in-memory text entries, and caller metadata
- offline package builder with SHA-256 manifest verification and path-safe staged extraction
- caller-driven command response tracker with explicit/quiet/fire-and-forget completion modes
- serial command executor combining transport writes, line decoding, timeout, and response tracking
- minimal Avalonia serial-port enumeration sample
- integrated .NET dependency, public API, and change-coupling gates under `tools/architecture`

## Composition model

The toolkit is a modular monolith distributed as one NuGet package. Namespaces such as `Commands`,
`Logging`, `Diagnostics`, and `Packaging` are responsibility boundaries, not independent deployables.
Applications may combine them directly and should keep policy, device protocol, UI, and lifecycle ownership
in the consuming application. See [ARCHITECTURE.md](https://github.com/DCRimbaud/dotnet-serial-toolkit/blob/main/ARCHITECTURE.md).

## Not included

- product command catalogs
- proprietary product protocols
- device training logic
- device-specific maps, calibration, or customer configuration
- product UI or branding

## Build

```powershell
dotnet build src\DCRimbaud.SerialToolkit\DCRimbaud.SerialToolkit.csproj -c Release
dotnet pack src\DCRimbaud.SerialToolkit\DCRimbaud.SerialToolkit.csproj -c Release -o artifacts\nuget
```

## License

MIT. See `LICENSE`.

## Code style

Public API and implementation comments are written in English.

## Author

Xue Weisen  
Email: xueweisen96@gmail.com  
GitHub: https://github.com/DCRimbaud
