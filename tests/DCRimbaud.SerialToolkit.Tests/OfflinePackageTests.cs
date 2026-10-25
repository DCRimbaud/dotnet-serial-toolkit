// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DCRimbaud.SerialToolkit.Packaging;
using Xunit;

namespace DCRimbaud.SerialToolkit.Tests;

public sealed class OfflinePackageTests
{
    /// <summary>Packages must round-trip through create, verify, and staged extraction.</summary>
    [Fact]
    public void CreatesVerifiesAndExtractsPackage()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "payload");
        Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.WriteAllText(Path.Combine(source, "bin", "app.txt"), "hello");
        var package = Path.Combine(temp.Path, "package.zip");
        OfflinePackage.Create(source, package, "1.0.0", "bin/app.txt");

        var verification = OfflinePackage.Verify(package);
        Assert.True(verification.IsValid, string.Join("; ", verification.Errors));
        var staging = Path.Combine(temp.Path, "staging");
        OfflinePackage.ExtractToStaging(package, staging);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(staging, "bin", "app.txt")));
    }

    /// <summary>Changing a payload byte after packaging must invalidate the SHA-256 check.</summary>
    [Fact]
    public void DetectsTamperedPayload()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "payload");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.txt"), "hello");
        var package = Path.Combine(temp.Path, "package.zip");
        OfflinePackage.Create(source, package, "1.0.0", "app.txt");

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("app.txt")!;
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("app.txt").Open(), new UTF8Encoding(false));
            writer.Write("HELLO");
        }

        var verification = OfflinePackage.Verify(package);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Errors, error => error.Contains("SHA-256 mismatch", StringComparison.Ordinal));
    }

    /// <summary>Manifest paths containing traversal segments must be rejected before extraction.</summary>
    [Fact]
    public void RejectsUnsafeManifestPath()
    {
        using var temp = new TemporaryDirectory();
        var package = Path.Combine(temp.Path, "unsafe.zip");
        var content = Encoding.UTF8.GetBytes("bad");
        var manifest = new PackageManifest(
            "1.0.0",
            DateTimeOffset.UtcNow,
            "../evil.txt",
            new[] { new PackageFileEntry("../evil.txt", content.Length, Convert.ToHexString(SHA256.HashData(content))) });

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var stream = archive.CreateEntry("evil.txt").Open())
            {
                stream.Write(content);
            }

            using var writer = new StreamWriter(
                archive.CreateEntry(OfflinePackage.ManifestFileName).Open(),
                new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(manifest));
        }

        var verification = OfflinePackage.Verify(package);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Errors, error => error.Contains("Invalid manifest path", StringComparison.Ordinal));
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
