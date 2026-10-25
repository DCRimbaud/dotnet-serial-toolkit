// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Globalization;
using System.Text;

namespace DCRimbaud.SerialToolkit.Logging;

/// <summary>
/// Writes newline-delimited JSON using daily and size-based file rotation.
/// The instance is not thread-safe; one consumer should own it at a time.
/// </summary>
public sealed class RotatingJsonlWriter : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly long _maxFileBytes;
    private readonly int _maxRetainedFiles;
    private StreamWriter? _writer;
    private FileStream? _stream;
    private DateTime _openDate = DateTime.MinValue;
    private long _currentFileBytes;
    private int _openSequence;

    /// <summary>Create a writer rooted at <paramref name="directory"/>.</summary>
    public RotatingJsonlWriter(
        string directory,
        string filePrefix,
        long maxFileBytes,
        int maxRetainedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedFiles, 1);
        _directory = Path.GetFullPath(directory);
        _filePrefix = filePrefix;
        _maxFileBytes = maxFileBytes;
        _maxRetainedFiles = maxRetainedFiles;
    }

    /// <summary>Path of the file currently being written, or null before the first record.</summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>Return the first segment path for a given date.</summary>
    public string GetBaseFilePath(DateTime date) =>
        Path.Combine(_directory, $"{_filePrefix}-{date:yyyyMMdd}.jsonl");

    /// <summary>Write one JSON record without embedded CR/LF characters.</summary>
    public void WriteLine(DateTime date, string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Contains('\n', StringComparison.Ordinal) || line.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A JSONL record cannot contain CR or LF.");
        }

        var localDate = date.Date;
        var bytes = Encoding.UTF8.GetByteCount(line) + 1;
        /* Rotate before writing the record that would cross the configured soft limit. */
        var exceedsCurrentFile = _currentFileBytes > 0 &&
                                 bytes > _maxFileBytes - _currentFileBytes;
        if (_writer is null || localDate != _openDate || exceedsCurrentFile)
        {
            var appendExistingBase = _writer is null || localDate != _openDate;
            OpenNextFile(localDate, appendExistingBase);
        }

        _writer!.Write(line);
        _writer.Write('\n');
        _currentFileBytes += bytes;
    }

    /// <summary>Flush accepted records to the operating system.</summary>
    public void Flush() => _writer?.Flush();

    /// <summary>Close the current file. Repeated calls are harmless.</summary>
    public void Dispose() => CloseWriter();

    private void OpenNextFile(DateTime date, bool appendExistingBase)
    {
        /* Reopen the base segment only when the same day continues after construction. */
        var nextSequence = appendExistingBase ? 0 : _openSequence + 1;
        CloseWriter();
        Directory.CreateDirectory(_directory);

        while (true)
        {
            var path = FilePathFor(date, nextSequence);
            long existingBytes = 0;
            var mode = FileMode.CreateNew;
            /* Append only to the unnumbered base file; numbered files are always new segments. */
            if (File.Exists(path))
            {
                if (appendExistingBase && nextSequence == 0)
                {
                    existingBytes = new FileInfo(path).Length;
                    if (existingBytes >= _maxFileBytes)
                    {
                        nextSequence++;
                        continue;
                    }

                    mode = FileMode.Append;
                }
                else
                {
                    nextSequence++;
                    continue;
                }
            }

            _stream = new FileStream(
                path,
                mode,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 1 << 16,
                FileOptions.SequentialScan);
            _writer = new StreamWriter(_stream, Utf8NoBom, bufferSize: 1 << 16);
            _openDate = date;
            _openSequence = nextSequence;
            _currentFileBytes = mode == FileMode.Append ? existingBytes : 0;
            CurrentFilePath = path;
            break;
        }

        ApplyRetention();
    }

    private string FilePathFor(DateTime date, int sequence)
    {
        var dateText = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return sequence == 0
            ? Path.Combine(_directory, $"{_filePrefix}-{dateText}.jsonl")
            : Path.Combine(_directory, $"{_filePrefix}-{dateText}-{sequence:D3}.jsonl");
    }

    private void ApplyRetention()
    {
        var files = Directory
            .EnumerateFiles(_directory, $"{_filePrefix}-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        /* Never delete the active segment, even when retention is temporarily over budget. */
        var remaining = files.Length;
        foreach (var file in files)
        {
            if (remaining <= _maxRetainedFiles)
            {
                break;
            }

            if (string.Equals(file.FullName, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                file.Delete();
                remaining--;
            }
            catch (IOException)
            {
                // A viewer may hold an old segment. Retry at the next rotation.
            }
            catch (UnauthorizedAccessException)
            {
                // Retention failure must not interrupt logging.
            }
        }
    }

    private void CloseWriter()
    {
        /* Detach fields before disposing so an exception cannot leave a half-closed writer active. */
        var writer = _writer;
        var stream = _stream;
        _writer = null;
        _stream = null;
        _openDate = DateTime.MinValue;
        _currentFileBytes = 0;
        CurrentFilePath = null;

        try
        {
            writer?.Dispose();
        }
        finally
        {
            stream?.Dispose();
        }
    }
}
