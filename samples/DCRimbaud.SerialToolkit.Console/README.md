# Console Command Sample

This sample is a host-runnable reference for one text command over `ISerialTransport`.
It composes `SerialCommandExecutor`, `SerialCommandResponseTracker`, and
`RotatingJsonlWriter` without requiring a development board.

## Default run

```powershell
dotnet run --project samples\DCRimbaud.SerialToolkit.Console\DCRimbaud.SerialToolkit.Console.csproj -c Release
```

The default path uses `ScriptedTransport`, which answers `PING` with `OK PONG`.
This mode is deterministic, suitable for CI, and demonstrates how to test command
logic without opening a serial port.

## Real serial port

```powershell
dotnet run --project samples\DCRimbaud.SerialToolkit.Console\DCRimbaud.SerialToolkit.Console.csproj -c Release -- --port COM7 --baud 115200 --command PING
```

The sample configures 8 data bits, no parity, one stop bit, and a 100 ms read
timeout. Change those settings in the consuming application when the device has a
different contract.

## Output

The command result is printed to the console and appended as one JSONL record under
`artifacts/console-sample/`. The record includes timestamp, command, status,
message, evidence, and transport type. Real applications should add their own
correlation IDs, session IDs, firmware version, and redaction policy.

## Reuse guidance

Copy the composition, not the scripted command catalog:

1. keep `ISerialTransport` as the boundary between command logic and hardware;
2. keep the ACK classifier in the application or protocol layer;
3. use `CommandResponsePolicy` to define timeout and completion semantics;
4. write structured records through a single-owned writer;
5. add reconnect, command gating, and session lifecycle in the application layer;
6. replace `ScriptedTransport` with a replayable fake or contract test for each
   product transport.

The sample intentionally does not own a reconnect loop, global command queue, or UI
thread. Those are application-level responsibilities.
