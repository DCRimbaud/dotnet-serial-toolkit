// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using DCRimbaud.SerialToolkit.Commands;
using DCRimbaud.SerialToolkit.Logging;

namespace DCRimbaud.SerialToolkit.ConsoleSample;

/// <summary>
/// Host-runnable example that executes one text command and records the result as JSONL.
/// The default path uses a scripted transport; <c>--port</c> selects a real serial port.
/// </summary>
internal static class Program
{
    private const string DefaultCommand = "PING";
    private const int DefaultBaudRate = 115200;

    /// <summary>
    /// Execute one serial command and write a structured result record.
    /// </summary>
    /// <param name="args">
    /// Optional <c>--command &lt;text&gt;</c>, <c>--port &lt;name&gt;</c>, and
    /// <c>--baud &lt;rate&gt;</c> arguments. With no arguments the sample uses
    /// the deterministic in-memory transport.
    /// </param>
    /// <returns>
    /// Zero when the command reaches <see cref="CommandResponseStatus.Success"/>,
    /// or one when it fails, times out, or the sample cannot start.
    /// </returns>
    /// <remarks>
    /// The default fake transport makes this executable useful as a CI smoke test and
    /// as a copyable integration skeleton. A real port uses the configured read timeout
    /// so the executor can evaluate its policy instead of blocking forever.
    /// </remarks>
    public static int Main(string[] args)
    {
        var command = GetOption(args, "--command");
        if (string.IsNullOrWhiteSpace(command))
        {
            command = DefaultCommand;
        }

        try
        {
            using var transport = CreateTransport(args);

            /* Opening is explicit so failures are reported before a command is written. */
            transport.Open();
            var executor = new SerialCommandExecutor(transport);
            var result = executor.Execute(command, CreatePolicy());
            var logDirectory = Path.Combine(
                Environment.CurrentDirectory,
                "artifacts",
                "console-sample");
            var logPath = WriteRunRecord(
                logDirectory,
                command,
                result,
                transport.GetType().Name);

            Console.WriteLine($"command={command}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"message={result.Message}");
            Console.WriteLine($"log={logPath}");

            return result.Status == CommandResponseStatus.Success ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"serial sample failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Create either the deterministic sample transport or a real serial-port adapter.
    /// </summary>
    /// <param name="args">Command-line arguments containing optional <c>--port</c> and <c>--baud</c> values.</param>
    /// <returns>An unopened transport owned by the caller.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <c>--port</c> has no value or <c>--baud</c> is not a positive integer.
    /// </exception>
    /// <remarks>
    /// Real ports use 8 data bits, no parity, one stop bit, and a 100 ms read timeout.
    /// Change those settings in the consuming application when the target device requires
    /// a different serial contract.
    /// </remarks>
    private static ISerialTransport CreateTransport(string[] args)
    {
        var portName = GetOption(args, "--port");
        if (string.IsNullOrWhiteSpace(portName))
        {
            return new ScriptedTransport();
        }

        var baudText = GetOption(args, "--baud");
        var baudRate = DefaultBaudRate;
        if (!string.IsNullOrWhiteSpace(baudText) &&
            (!int.TryParse(baudText, out baudRate) || baudRate <= 0))
        {
            throw new ArgumentException("--baud must be a positive integer.", nameof(args));
        }

        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.UTF8,
            NewLine = "\n",
            ReadTimeout = 100,
            WriteTimeout = 100,
        };
        return new SerialPortTransport(port);
    }

    /// <summary>
    /// Build the explicit-ACK policy used by the sample command.
    /// </summary>
    /// <returns>
    /// A policy with a two-second overall timeout, a 100 ms quiet threshold, and
    /// line classification for <c>OK</c> and <c>ERR</c> responses.
    /// </returns>
    /// <remarks>
    /// The quiet threshold is retained even though this sample uses explicit ACK;
    /// a future command can switch to <see cref="CommandCompletionKind.QuietOutput"/>
    /// without changing the executor.
    /// </remarks>
    private static CommandResponsePolicy CreatePolicy() =>
        new(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(100),
            CommandCompletionKind.ExplicitAck,
            ClassifyResponse);

    /// <summary>
    /// Classify one response line for the sample's explicit-ACK command contract.
    /// </summary>
    /// <param name="line">Complete line without CR/LF, supplied by the command tracker.</param>
    /// <returns>
    /// <see cref="CommandLineDisposition.Success"/> for <c>OK</c>,
    /// <see cref="CommandLineDisposition.Failure"/> for <c>ERR</c>, and
    /// <see cref="CommandLineDisposition.Neutral"/> for all other lines.
    /// </returns>
    /// <remarks>
    /// The classifier is intentionally product-neutral. A real application should map
    /// its own ACK, NAK, status, and progress lines here without moving protocol policy
    /// into the serial transport.
    /// </remarks>
    private static CommandLineDisposition ClassifyResponse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineDisposition.Success;
        }

        if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineDisposition.Failure;
        }

        return CommandLineDisposition.Neutral;
    }

    /// <summary>
    /// Read the value following a named command-line option.
    /// </summary>
    /// <param name="args">Raw process arguments; null and empty inputs return null.</param>
    /// <param name="name">Option name including the leading dashes.</param>
    /// <returns>The following argument, or null when the option is absent or has no value.</returns>
    /// <remarks>
    /// Matching is case-insensitive and the helper does not mutate the argument array.
    /// </remarks>
    private static string? GetOption(string[] args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Append one command result to a rotating JSONL run log.
    /// </summary>
    /// <param name="directory">Directory that receives the JSONL segment.</param>
    /// <param name="command">Command text written to the transport.</param>
    /// <param name="result">Terminal result and retained response evidence.</param>
    /// <param name="transportName">Concrete transport type used for this run.</param>
    /// <returns>Absolute path of the active JSONL segment.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the log writer does not expose a path after a successful write.
    /// </exception>
    /// <remarks>
    /// The record intentionally contains only product-neutral fields. Add timestamps,
    /// correlation IDs, and device identity in the consuming application's log schema.
    /// </remarks>
    private static string WriteRunRecord(
        string directory,
        string command,
        CommandResponseResult result,
        string transportName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportName);

        using var writer = new RotatingJsonlWriter(
            directory,
            "serial-sample",
            maxFileBytes: 1 << 20,
            maxRetainedFiles: 4);
        var record = new
        {
            timestamp = DateTimeOffset.UtcNow,
            command,
            status = result.Status.ToString(),
            message = result.Message,
            evidence = result.Evidence,
            transport = transportName,
        };
        writer.WriteLine(DateTime.UtcNow, JsonSerializer.Serialize(record));
        writer.Flush();

        if (string.IsNullOrWhiteSpace(writer.CurrentFilePath))
        {
            throw new InvalidOperationException("The JSONL writer did not expose an active path.");
        }

        return writer.CurrentFilePath;
    }

    /// <summary>
    /// Deterministic serial transport that answers a small demonstration command set.
    /// </summary>
    /// <remarks>
    /// The transport is intentionally single-threaded and host-only. It proves that
    /// command execution and response tracking can be tested without a physical UART.
    /// </remarks>
    private sealed class ScriptedTransport : ISerialTransport
    {
        private readonly Queue<byte> _responseBytes = new();
        private readonly List<string> _writtenCommands = new();

        /// <summary>
        /// Create a closed scripted transport with no queued response bytes.
        /// </summary>
        /// <remarks>The caller must call <see cref="Open"/> before writing a command.</remarks>
        public ScriptedTransport()
        {
        }

        /// <inheritdoc />
        public bool IsOpen { get; private set; }

        /// <inheritdoc />
        public Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Commands accepted by the fake transport, in call order.
        /// </summary>
        /// <remarks>The returned list is a live read-only view intended for diagnostics and tests.</remarks>
        public IReadOnlyList<string> WrittenCommands => _writtenCommands;
        /// <summary>
        /// Mark the scripted transport open so the command executor accepts writes.
        /// </summary>
        /// <remarks>Repeated calls are harmless.</remarks>
        public void Open()
        {
            IsOpen = true;
        }

        /// <summary>
        /// Read queued response bytes or emulate an exhausted serial read timeout.
        /// </summary>
        /// <param name="buffer">Destination buffer; must be non-null.</param>
        /// <param name="offset">First writable index in <paramref name="buffer"/>.</param>
        /// <param name="count">Maximum number of bytes to copy.</param>
        /// <returns>Number of bytes copied, always greater than zero when it returns.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the requested range is outside the buffer.</exception>
        /// <exception cref="TimeoutException">
        /// Thrown after a short simulated wait when no response bytes remain.
        /// </exception>
        /// <remarks>
        /// The executor treats <see cref="TimeoutException"/> as a signal to evaluate
        /// policy time rather than as an immediate command failure.
        /// </remarks>
        public int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested buffer range is invalid.");
            }

            if (_responseBytes.Count == 0)
            {
                Thread.Sleep(5);
                throw new TimeoutException("No scripted response is currently available.");
            }

            var copied = 0;
            while (copied < count && _responseBytes.Count > 0)
            {
                buffer[offset + copied] = _responseBytes.Dequeue();
                copied++;
            }

            return copied;
        }

        /// <summary>
        /// Record one text command and enqueue a deterministic response line.
        /// </summary>
        /// <param name="text">Command text including any trailing newline.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the transport is closed.</exception>
        /// <remarks>
        /// The fake response vocabulary is deliberately small: <c>PING</c> returns
        /// <c>OK PONG</c>, <c>STAT</c> returns a value and an ACK, and everything else
        /// returns <c>ERR unsupported</c>.
        /// </remarks>
        public void Write(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (!IsOpen)
            {
                throw new InvalidOperationException("The scripted transport must be open.");
            }

            _writtenCommands.Add(text);
            switch (text.Trim())
            {
                case "PING":
                    QueueResponse("OK PONG\n");
                    break;
                case "STAT":
                    QueueResponse("VALUE 42\nOK\n");
                    break;
                default:
                    QueueResponse("ERR unsupported\n");
                    break;
            }
        }

        /// <summary>
        /// Reject binary writes because this sample demonstrates the text command path.
        /// </summary>
        /// <param name="buffer">Ignored binary payload.</param>
        /// <param name="offset">Ignored payload offset.</param>
        /// <param name="count">Ignored payload length.</param>
        /// <exception cref="NotSupportedException">Always thrown by this host sample.</exception>
        /// <remarks>
        /// A production transport should implement the same binary contract used by
        /// its protocol and framing layer rather than silently dropping bytes.
        /// </remarks>
        public void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The console sample only demonstrates text commands.");
        }

        /// <summary>
        /// Mark the scripted transport closed; queued response bytes remain available
        /// until the caller reopens or disposes the transport.
        /// </summary>
        /// <remarks>Repeated calls are harmless.</remarks>
        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>
        /// Release the sample transport by closing it.
        /// </summary>
        /// <remarks>No operating-system handle exists in this in-memory fake.</remarks>
        public void Dispose()
        {
            Close();
        }

        /// <summary>
        /// Encode and enqueue one scripted response line.
        /// </summary>
        /// <param name="text">Response text; the caller owns the string and no reference is retained.</param>
        /// <remarks>
        /// UTF-8 bytes are queued one at a time so partial-read behavior remains
        /// representative of a real serial transport.
        /// </remarks>
        private void QueueResponse(string text)
        {
            foreach (var value in Encoding.UTF8.GetBytes(text))
            {
                _responseBytes.Enqueue(value);
            }
        }
    }
}