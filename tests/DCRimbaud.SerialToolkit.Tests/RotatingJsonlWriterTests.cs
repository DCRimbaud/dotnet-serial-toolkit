// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Text;
using DCRimbaud.SerialToolkit.Logging;
using Xunit;

namespace DCRimbaud.SerialToolkit.Tests;

public sealed class RotatingJsonlWriterTests
{
    /// <summary>
    /// Writes must be UTF-8 without BOM and use LF only so downstream JSONL readers can parse one record per line.
    /// </summary>
    [Fact]
    public void WritesUtf8NoBomWithLf()
    {
        using var directory = new TemporaryDirectory();
        var date = new DateTime(2026, 10, 26);
        using (var writer = new RotatingJsonlWriter(directory.Path, "events", 1024, 3))
        {
            writer.WriteLine(date, "{\"value\":1}");
            writer.WriteLine(date, "{\"value\":2}");
            writer.Flush();
        }

        var file = Assert.Single(Directory.GetFiles(directory.Path, "*.jsonl"));
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>
    /// A new date must open a separate segment instead of appending records to the previous day.
    /// </summary>
    [Fact]
    public void RotatesOnDateChange()
    {
        using var directory = new TemporaryDirectory();
        using (var writer = new RotatingJsonlWriter(directory.Path, "events", 1024, 3))
        {
            writer.WriteLine(new DateTime(2026, 10, 25), "{\"value\":1}");
            writer.WriteLine(new DateTime(2026, 10, 26), "{\"value\":2}");
        }

        Assert.Equal(2, Directory.GetFiles(directory.Path, "*.jsonl").Length);
    }

    /// <summary>
    /// A record that would exceed the soft byte limit must open the next numbered segment.
    /// </summary>
    [Fact]
    public void RotatesOnSizeLimit()
    {
        using var directory = new TemporaryDirectory();
        using (var writer = new RotatingJsonlWriter(directory.Path, "events", 20, 3))
        {
            writer.WriteLine(new DateTime(2026, 10, 26), "{\"value\":\"1234567890\"}");
            writer.WriteLine(new DateTime(2026, 10, 26), "{\"value\":\"abcdefghij\"}");
        }

        Assert.True(Directory.GetFiles(directory.Path, "*.jsonl").Length >= 2);
    }

    /// <summary>
    /// Embedded line breaks must be rejected before any partial record reaches disk.
    /// </summary>
    [Fact]
    public void RejectsEmbeddedNewline()
    {
        using var directory = new TemporaryDirectory();
        using var writer = new RotatingJsonlWriter(directory.Path, "events", 1024, 3);
        Assert.Throws<InvalidOperationException>(() => writer.WriteLine(DateTime.Today, "a\nb"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DCRimbaud.SerialToolkit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
