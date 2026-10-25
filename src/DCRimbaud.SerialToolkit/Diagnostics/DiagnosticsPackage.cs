// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DCRimbaud.SerialToolkit.Diagnostics;

/// <summary>One file recorded in a diagnostics package.</summary>
public sealed record DiagnosticsFileEntry(string EntryName, long Length);

/// <summary>Stable metadata written into a diagnostics package.</summary>
public sealed record DiagnosticsManifest(
    string Schema,
    DateTimeOffset CreatedAt,
    string MachineName,
    string OperatingSystem,
    string LibraryVersion,
    IReadOnlyList<DiagnosticsFileEntry> Files,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Result returned after creating a diagnostics package.</summary>
public sealed record DiagnosticsPackageResult(
    string OutputPath,
    DiagnosticsManifest Manifest);

/// <summary>
/// Creates a diagnostics ZIP from source directories plus optional in-memory text entries.
/// No product protocol or device-specific fields are added by this library.
/// </summary>
public static class DiagnosticsPackage
{
    /// <summary>Manifest file name inside the ZIP.</summary>
    public const string ManifestFileName = "diagnostics-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Create a diagnostics package.</summary>
    /// <param name="outputZipPath">Destination ZIP path.</param>
    /// <param name="sourceDirectories">Directories to include recursively.</param>
    /// <param name="textEntries">Optional in-memory text files keyed by ZIP-relative path.</param>
    /// <param name="metadata">Optional application-specific key/value facts.</param>
    public static DiagnosticsPackageResult Create(
        string outputZipPath,
        IEnumerable<string> sourceDirectories,
        IReadOnlyDictionary<string, string>? textEntries = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputZipPath);
        ArgumentNullException.ThrowIfNull(sourceDirectories);

        var outputFullPath = Path.GetFullPath(outputZipPath);
        /* Normalize and deduplicate sources before assigning archive roots. */
        var sources = sourceDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var files = new List<DiagnosticsFileEntry>();
        using (var archive = ZipFile.Open(outputFullPath, ZipArchiveMode.Create))
        {
            /* Distinct roots prevent file entries from different directories colliding. */
            var usedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                if (!Directory.Exists(source))
                {
                    continue;
                }

                var rootName = MakeUniqueRootName(new DirectoryInfo(source).Name, usedRoots);
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    /* Never include the ZIP that is currently being created. */
                    if (string.Equals(Path.GetFullPath(file), outputFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    var entryName = $"{rootName}/{relative}";
                    AddFileEntry(archive, file, entryName);
                    files.Add(new DiagnosticsFileEntry(entryName, new FileInfo(file).Length));
                }
            }

            if (textEntries is not null)
            {
                foreach (var pair in textEntries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    AddTextEntry(archive, pair.Key, pair.Value);
                    files.Add(new DiagnosticsFileEntry(pair.Key, Encoding.UTF8.GetByteCount(pair.Value)));
                }
            }

            /* Build the manifest last so it describes every file and in-memory entry. */
            var manifest = new DiagnosticsManifest(
                Schema: "dcrimbaud.diagnostics.v1",
                CreatedAt: DateTimeOffset.UtcNow,
                MachineName: Environment.MachineName,
                OperatingSystem: Environment.OSVersion.ToString(),
                LibraryVersion: typeof(DiagnosticsPackage).Assembly.GetName().Version?.ToString() ?? "unknown",
                Files: files.OrderBy(file => file.EntryName, StringComparer.Ordinal).ToArray(),
                Metadata: metadata is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal));

            WriteJsonEntry(archive, ManifestFileName, manifest);
            return new DiagnosticsPackageResult(outputFullPath, manifest);
        }
    }

    private static string MakeUniqueRootName(string requested, HashSet<string> usedRoots)
    {
        var root = string.IsNullOrWhiteSpace(requested) ? "source" : requested;
        if (usedRoots.Add(root))
        {
            return root;
        }

        var index = 2;
        while (!usedRoots.Add($"{root}-{index}"))
        {
            index++;
        }

        return $"{root}-{index}";
    }

    private static void AddFileEntry(ZipArchive archive, string sourcePath, string entryName)
    {
        using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1 << 16,
            FileOptions.SequentialScan);
        using var output = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
        input.CopyTo(output);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        ArgumentNullException.ThrowIfNull(text);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T value)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }
}
