// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using DCRimbaud.SerialToolkit.Commands;
using Xunit;

namespace DCRimbaud.SerialToolkit.Tests;

public sealed class SerialCommandResponseTrackerTests
{
    /// <summary>An explicit ACK must complete the command successfully.</summary>
    [Fact]
    public void CompletesOnSuccessLine()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                CommandCompletionKind.ExplicitAck,
                line => line == "OK" ? CommandLineDisposition.Success : CommandLineDisposition.Neutral));

        var result = tracker.ObserveLine("OK", start.AddMilliseconds(20));
        Assert.Equal(CommandResponseStatus.Success, result.Status);
        Assert.True(result.IsTerminal);
        Assert.Contains("OK", result.Evidence);
    }

    /// <summary>A failure line must end the command even before the timeout.</summary>
    [Fact]
    public void CompletesOnFailureLine()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                CommandCompletionKind.ExplicitAck,
                line => line == "FAIL" ? CommandLineDisposition.Failure : CommandLineDisposition.Neutral));

        var result = tracker.ObserveLine("FAIL", start.AddMilliseconds(20));
        Assert.Equal(CommandResponseStatus.Failure, result.Status);
    }

    /// <summary>The tracker must report timeout when no terminal response arrives.</summary>
    [Fact]
    public void ReportsTimeout()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                CommandCompletionKind.ExplicitAck,
                _ => CommandLineDisposition.Neutral));

        var result = tracker.Evaluate(start.AddSeconds(1));
        Assert.Equal(CommandResponseStatus.TimedOut, result.Status);
    }

    /// <summary>Quiet-output completion must wait for the configured quiet window.</summary>
    [Fact]
    public void CompletesAfterQuietWindow()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                CommandCompletionKind.QuietOutput));

        tracker.ObserveLine("value", start.AddMilliseconds(20));
        Assert.Equal(CommandResponseStatus.Pending, tracker.Evaluate(start.AddMilliseconds(119)).Status);
        Assert.Equal(CommandResponseStatus.Success, tracker.Evaluate(start.AddMilliseconds(120)).Status);
    }

    /// <summary>Fire-and-forget commands must be terminal immediately.</summary>
    [Fact]
    public void FireAndForgetCompletesImmediately()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero,
                CommandCompletionKind.FireAndForget));

        Assert.Equal(CommandResponseStatus.Success, tracker.Snapshot().Status);
    }

    /// <summary>Evidence must be capped to prevent unbounded memory growth.</summary>
    [Fact]
    public void CapsEvidence()
    {
        var start = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var tracker = new SerialCommandResponseTracker(
            start,
            new CommandResponsePolicy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                CommandCompletionKind.QuietOutput,
                MaxEvidenceLines: 3));

        for (var i = 0; i < 10; i++)
        {
            tracker.ObserveLine($"line-{i}", start.AddMilliseconds(i));
        }

        var result = tracker.Snapshot();
        Assert.Equal(3, result.Evidence.Count);
        Assert.Equal(new[] { "line-7", "line-8", "line-9" }, result.Evidence);
    }
}
