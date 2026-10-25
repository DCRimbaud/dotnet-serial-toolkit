// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
namespace DCRimbaud.SerialToolkit.Commands;

/// <summary>How a command response is considered complete.</summary>
public enum CommandCompletionKind
{
    /// <summary>Wait for a classifier line to mark success or failure.</summary>
    ExplicitAck,
    /// <summary>Complete after the configured quiet period.</summary>
    QuietOutput,
    /// <summary>Complete immediately after the command is started.</summary>
    FireAndForget
}

/// <summary>Classification applied to one response line.</summary>
public enum CommandLineDisposition
{
    /// <summary>The line is evidence but does not complete the command.</summary>
    Neutral,
    /// <summary>The line proves successful completion.</summary>
    Success,
    /// <summary>The line proves failure completion.</summary>
    Failure
}

/// <summary>Observable state of a command response.</summary>
public enum CommandResponseStatus
{
    Pending,
    Success,
    Failure,
    TimedOut,
    Cancelled
}

/// <summary>Policy controlling completion and evidence retention.</summary>
public sealed record CommandResponsePolicy(
    TimeSpan Timeout,
    TimeSpan QuietPeriod,
    CommandCompletionKind Completion = CommandCompletionKind.ExplicitAck,
    Func<string, CommandLineDisposition>? ClassifyLine = null,
    int MaxEvidenceLines = 200)
{
    /// <summary>Validate policy values before a tracker is created.</summary>
    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        }

        if (QuietPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(QuietPeriod));
        }

        if (MaxEvidenceLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEvidenceLines));
        }

        if (Completion == CommandCompletionKind.ExplicitAck && ClassifyLine is null)
        {
            throw new ArgumentException("ExplicitAck requires a line classifier.", nameof(ClassifyLine));
        }
    }
}

/// <summary>Current command response result and retained evidence.</summary>
public sealed record CommandResponseResult(
    CommandResponseStatus Status,
    string Message,
    IReadOnlyList<string> Evidence)
{
    /// <summary>True when the command reached a terminal state.</summary>
    public bool IsTerminal => Status != CommandResponseStatus.Pending;
}

/// <summary>
/// Tracks lines for one command using caller-supplied timestamps.
/// This class does not own a timer; call <see cref="Evaluate(DateTimeOffset)"/> from the caller loop.
/// </summary>
public sealed class SerialCommandResponseTracker
{
    private readonly object _gate = new();
    private readonly CommandResponsePolicy _policy;
    private readonly DateTimeOffset _startedAt;
    private readonly List<string> _evidence = new();
    private DateTimeOffset _lastLineAt;
    private CommandResponseStatus _status;
    private string _message = "pending";

    /// <summary>Start tracking one command.</summary>
    public SerialCommandResponseTracker(DateTimeOffset startedAt, CommandResponsePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        _policy = policy;
        _startedAt = startedAt;
        _lastLineAt = startedAt;

        // Fire-and-forget commands complete as soon as the caller starts them.
        _status = policy.Completion == CommandCompletionKind.FireAndForget
            ? CommandResponseStatus.Success
            : CommandResponseStatus.Pending;
        if (_status == CommandResponseStatus.Success)
        {
            _message = "fire-and-forget";
        }
    }

    /// <summary>Record one response line and update terminal state.</summary>
    public CommandResponseResult ObserveLine(string line, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            if (_status != CommandResponseStatus.Pending)
            {
                return Snapshot();
            }

            _lastLineAt = observedAt;
            _evidence.Add(line);
            TrimEvidence();

            if (_policy.ClassifyLine is not null)
            {
                var disposition = _policy.ClassifyLine(line);
                if (disposition == CommandLineDisposition.Success)
                {
                    _status = CommandResponseStatus.Success;
                    _message = "response classified as success";
                }
                else if (disposition == CommandLineDisposition.Failure)
                {
                    _status = CommandResponseStatus.Failure;
                    _message = "response classified as failure";
                }
            }

            return Snapshot();
        }
    }

    /// <summary>Advance timeout and quiet-period evaluation using the current timestamp.</summary>
    public CommandResponseResult Evaluate(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_status != CommandResponseStatus.Pending)
            {
                return Snapshot();
            }

            if (_policy.Completion == CommandCompletionKind.QuietOutput &&
                now - _lastLineAt >= _policy.QuietPeriod)
            {
                _status = CommandResponseStatus.Success;
                _message = "quiet period elapsed";
            }
            else if (now - _startedAt >= _policy.Timeout)
            {
                _status = CommandResponseStatus.TimedOut;
                _message = "command timed out";
            }

            return Snapshot();
        }
    }

    /// <summary>Cancel a pending command.</summary>
    public CommandResponseResult Cancel()
    {
        lock (_gate)
        {
            if (_status == CommandResponseStatus.Pending)
            {
                _status = CommandResponseStatus.Cancelled;
                _message = "command cancelled";
            }

            return Snapshot();
        }
    }

    /// <summary>Get the current result without advancing time.</summary>
    public CommandResponseResult Snapshot()
    {
        lock (_gate)
        {
            return new CommandResponseResult(_status, _message, _evidence.ToArray());
        }
    }

    private void TrimEvidence()
    {
        // Cap evidence so a noisy device cannot grow memory without bound.
        if (_evidence.Count <= _policy.MaxEvidenceLines)
        {
            return;
        }

        _evidence.RemoveRange(0, _evidence.Count - _policy.MaxEvidenceLines);
    }
}
