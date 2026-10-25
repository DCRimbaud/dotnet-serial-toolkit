// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Text;

namespace DCRimbaud.SerialToolkit.Commands;

/// <summary>
/// Executes one text command over an open <see cref="ISerialTransport"/> and tracks response lines.
/// The class is product-neutral and does not own reconnect or command-catalog policy.
/// </summary>
public sealed class SerialCommandExecutor
{
    private readonly ISerialTransport _transport;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _readBufferSize;

    /// <summary>Create an executor over an open transport.</summary>
    public SerialCommandExecutor(
        ISerialTransport transport,
        Func<DateTimeOffset>? clock = null,
        int readBufferSize = 512)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentOutOfRangeException.ThrowIfLessThan(readBufferSize, 1);
        _transport = transport;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _readBufferSize = readBufferSize;
    }

    /// <summary>
    /// Send one command followed by a newline and read until the policy reaches a terminal state.
    /// </summary>
    public CommandResponseResult Execute(
        string command,
        CommandResponsePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(policy);
        if (!_transport.IsOpen)
        {
            throw new InvalidOperationException("Serial transport must be open before executing a command.");
        }

        var tracker = new SerialCommandResponseTracker(_clock(), policy);
        _transport.Write(command + "\n");
        if (tracker.Snapshot().IsTerminal)
        {
            return tracker.Snapshot();
        }

        var decoder = _transport.Encoding.GetDecoder();
        var bytes = new byte[_readBufferSize];
        var chars = new char[_transport.Encoding.GetMaxCharCount(bytes.Length)];
        var line = new StringBuilder();

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return tracker.Cancel();
            }

            var read = 0;
            try
            {
                read = _transport.Read(bytes, 0, bytes.Length);
            }
            catch (TimeoutException)
            {
                /* Timeout means "evaluate now"; it does not automatically mean command failure. */
            }

            if (read > 0)
            {
                var charCount = decoder.GetChars(bytes, 0, read, chars, 0);
                for (var index = 0; index < charCount; index++)
                {
                    var value = chars[index];
                    if (value == '\r')
                    {
                        continue;
                    }

                    if (value == '\n')
                    {
                        var lineResult = tracker.ObserveLine(line.ToString(), _clock());
                        line.Clear();
                        if (lineResult.IsTerminal)
                        {
                            return lineResult;
                        }
                    }
                    else
                    {
                        line.Append(value);
                    }
                }
            }

            var result = tracker.Evaluate(_clock());
            if (result.IsTerminal)
            {
                return result;
            }
        }
    }
}
