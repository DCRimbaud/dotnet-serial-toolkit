// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Compression;
using System.Text.Json;
using DCRimbaud.SerialToolkit.Diagnostics;
using Xunit;

namespace DCRimbaud.SerialToolkit.Tests;

public sealed class DiagnosticsPackageTests
{
    /// <summary>
    /// Source files and in-memory text entries must both be present in the archive and manifest.
    /// </summary>
    [Fact]
    public void CreatesArchiveWithManifestAndTextEntry()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "logs");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "session.jsonl"), "{\"a\":1}");

        var output = Path.Combine(temp.Path, "diagnostics.zip");
        var result = DiagnosticsPackage.Create(
            output,
            [source],
            new Dictionary<string, string> { ["diagnostics-session.jsonl"] = "{\"snapshot\":1}\n" },
            new Dictionary<string, string> { ["product"] = "test" });

        Assert.True(File.Exists(output));
        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry("logs/session.jsonl"));
        Assert.NotNull(archive.GetEntry("diagnostics-session.jsonl"));
        var manifestEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(DiagnosticsPackage.ManifestFileName));
        using var stream = manifestEntry.Open();
        using var document = JsonDocument.Parse(stream);
        Assert.Equal("dcrimbaud.diagnostics.v1", document.RootElement.GetProperty("schema").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal("test", document.RootElement.GetProperty("metadata").GetProperty("product").GetString());
        Assert.Equal(2, result.Manifest.Files.Count);
    }

    /// <summary>
    /// Duplicate source directory names must receive deterministic distinct roots to avoid ZIP entry collisions.
    /// </summary>
    [Fact]
    public void DeduplicatesArchiveRootNames()
    {
        using var temp = new TemporaryDirectory();
        var first = Path.Combine(temp.Path, "a", "logs");
        var second = Path.Combine(temp.Path, "b", "logs");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "one.txt"), "1");
        File.WriteAllText(Path.Combine(second, "two.txt"), "2");

        var output = Path.Combine(temp.Path, "diagnostics.zip");
        var result = DiagnosticsPackage.Create(output, [first, second]);

        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry("logs/one.txt"));
        Assert.NotNull(archive.GetEntry("logs-2/two.txt"));
        Assert.Equal(2, result.Manifest.Files.Count);
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
