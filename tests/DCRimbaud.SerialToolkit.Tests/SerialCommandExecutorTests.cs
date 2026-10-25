// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Text;
using DCRimbaud.SerialToolkit.Commands;
using Xunit;

namespace DCRimbaud.SerialToolkit.Tests;

public sealed class SerialCommandExecutorTests
{
    /// <summary>An explicit ACK line must complete the command successfully.</summary>
    [Fact]
    public void ExecutesCommandAndReadsExplicitAck()
    {
        var transport = new FakeTransport("booting\nOK\n");
        var executor = new SerialCommandExecutor(transport);
        var policy = new CommandResponsePolicy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(50),
            CommandCompletionKind.ExplicitAck,
            line => line == "OK" ? CommandLineDisposition.Success : CommandLineDisposition.Neutral);

        var result = executor.Execute("stat", policy);

        Assert.Equal(CommandResponseStatus.Success, result.Status);
        Assert.Contains("stat\n", transport.WrittenText.ToString());
        Assert.Contains("OK", result.Evidence);
    }

    /// <summary>A quiet-output command must complete after the quiet window.</summary>
    [Fact]
    public void ExecutesQuietOutputCommand()
    {
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var transport = new FakeTransport("value\n");
        var executor = new SerialCommandExecutor(
            transport,
            () => now = now.AddMilliseconds(100));
        var policy = new CommandResponsePolicy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(100),
            CommandCompletionKind.QuietOutput);

        var result = executor.Execute("read", policy);

        Assert.Equal(CommandResponseStatus.Success, result.Status);
        Assert.Contains("value", result.Evidence);
    }

    /// <summary>A command with no response must report timeout.</summary>
    [Fact]
    public void ReportsTimeoutWhenNoResponseArrives()
    {
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var transport = new EmptyTimeoutTransport();
        var executor = new SerialCommandExecutor(
            transport,
            () => now = now.AddMilliseconds(200));
        var policy = new CommandResponsePolicy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            CommandCompletionKind.ExplicitAck,
            _ => CommandLineDisposition.Neutral);

        var result = executor.Execute("query", policy);

        Assert.Equal(CommandResponseStatus.TimedOut, result.Status);
    }

    private sealed class FakeTransport : ISerialTransport
    {
        private readonly byte[] _response;
        private int _offset;

        public FakeTransport(string response)
        {
            _response = Encoding.UTF8.GetBytes(response);
        }

        public bool IsOpen => true;
        public Encoding Encoding => Encoding.UTF8;
        public StringBuilder WrittenText { get; } = new();

        public void Open() { }
        public void Write(string text) => WrittenText.Append(text);
        public void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public void Close() { }
        public void Dispose() { }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_offset >= _response.Length)
            {
                throw new TimeoutException();
            }

            var copied = Math.Min(count, _response.Length - _offset);
            Array.Copy(_response, _offset, buffer, offset, copied);
            _offset += copied;
            return copied;
        }
    }

    private sealed class EmptyTimeoutTransport : ISerialTransport
    {
        public bool IsOpen => true;
        public Encoding Encoding => Encoding.UTF8;
        public void Open() { }
        public void Write(string text) { }
        public void Write(byte[] buffer, int offset, int count) { }
        public int Read(byte[] buffer, int offset, int count) => throw new TimeoutException();
        public void Close() { }
        public void Dispose() { }
    }
}
