// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DCRimbaud.SerialToolkit.Packaging;

/// <summary>One regular file declared by an offline package manifest.</summary>
public sealed record PackageFileEntry(string RelativePath, long Length, string Sha256);

/// <summary>Manifest embedded in an offline package.</summary>
public sealed record PackageManifest(
    string PackageVersion,
    DateTimeOffset CreatedAt,
    string EntryPoint,
    IReadOnlyList<PackageFileEntry> Files);

/// <summary>Result of verifying a package before extraction.</summary>
public sealed record PackageVerificationResult(
    bool IsValid,
    PackageManifest? Manifest,
    IReadOnlyList<string> Errors)
{
    /// <summary>Create a failed result with one error.</summary>
    public static PackageVerificationResult Invalid(string error) =>
        new(false, null, new[] { error });
}

/// <summary>
/// Creates, verifies, and safely extracts ZIP packages with a SHA-256 manifest.
/// The package format is application-neutral; callers provide version and entry-point semantics.
/// </summary>
public static class OfflinePackage
{
    /// <summary>Manifest file name inside the ZIP.</summary>
    public const string ManifestFileName = "package-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Create a package from a directory.</summary>
    public static PackageManifest Create(
        string sourceDirectory,
        string outputZipPath,
        string packageVersion,
        string entryPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputZipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var outputFullPath = Path.GetFullPath(outputZipPath);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), outputFullPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PackageFileEntry(
                Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                ComputeSha256(path)))
            .ToArray();

        if (!TryNormalizeRelativePath(entryPoint, out _, out var entryError))
        {
            throw new ArgumentException(entryError, nameof(entryPoint));
        }

        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var manifest = new PackageManifest(packageVersion, DateTimeOffset.UtcNow, entryPoint, files);
        using var archive = ZipFile.Open(outputFullPath, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var sourcePath = Path.Combine(sourceRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
            using var source = File.OpenRead(sourcePath);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }

        WriteJsonEntry(archive, ManifestFileName, manifest);
        return manifest;
    }

    /// <summary>Verify manifest structure, archive membership, lengths, hashes, and path safety.</summary>
    public static PackageVerificationResult Verify(string packageZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageZipPath);
        if (!File.Exists(packageZipPath))
        {
            return PackageVerificationResult.Invalid($"Package not found: {packageZipPath}");
        }

        try
        {
            using var archive = ZipFile.OpenRead(packageZipPath);
            var manifestEntry = archive.GetEntry(ManifestFileName);
            if (manifestEntry is null)
            {
                return PackageVerificationResult.Invalid($"Package is missing {ManifestFileName}.");
            }

            using var manifestStream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestStream, JsonOptions);
            if (manifest is null || manifest.Files is null)
            {
                return PackageVerificationResult.Invalid("Package manifest cannot be parsed.");
            }

            var errors = new List<string>();
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (!TryNormalizeRelativePath(file.RelativePath, out var normalized, out var pathError))
                {
                    errors.Add($"Invalid manifest path '{file.RelativePath}': {pathError}");
                    continue;
                }

                if (!declared.Add(normalized))
                {
                    errors.Add($"Duplicate manifest path: {normalized}");
                }
            }

            if (!TryNormalizeRelativePath(manifest.EntryPoint, out var normalizedEntry, out var entryError) ||
                !string.Equals(manifest.EntryPoint, manifest.EntryPoint.Trim(), StringComparison.Ordinal))
            {
                errors.Add($"Invalid entry point '{manifest.EntryPoint}': {entryError}");
            }
            else if (!declared.Contains(normalizedEntry))
            {
                errors.Add($"Entry point is not declared in files: {normalizedEntry}");
            }

            var archiveFiles = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) ||
                    string.Equals(entry.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryNormalizeRelativePath(entry.FullName, out var normalized, out var pathError))
                {
                    errors.Add($"Unsafe archive path '{entry.FullName}': {pathError}");
                    continue;
                }

                if (!archiveFiles.TryAdd(normalized, entry))
                {
                    errors.Add($"Duplicate archive entry: {normalized}");
                }
            }

            foreach (var missing in declared.Except(archiveFiles.Keys, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Declared file is missing from archive: {missing}");
            }

            foreach (var extra in archiveFiles.Keys.Except(declared, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Archive contains undeclared file: {extra}");
            }

            foreach (var file in manifest.Files)
            {
                if (!TryNormalizeRelativePath(file.RelativePath, out var normalized, out _) ||
                    !archiveFiles.TryGetValue(normalized, out var entry))
                {
                    continue;
                }

                if (entry.Length != file.Length)
                {
                    errors.Add($"Length mismatch: {normalized}");
                    continue;
                }

                using var stream = entry.Open();
                var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"SHA-256 mismatch: {normalized}");
                }
            }

            return errors.Count == 0
                ? new PackageVerificationResult(true, manifest, Array.Empty<string>())
                : new PackageVerificationResult(false, manifest, errors.ToArray());
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            return PackageVerificationResult.Invalid($"Package verification failed: {exception.Message}");
        }
    }

    /// <summary>Verify then extract declared files into a new staging directory.</summary>
    public static PackageManifest ExtractToStaging(string packageZipPath, string stagingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageZipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        var verification = Verify(packageZipPath);
        if (!verification.IsValid || verification.Manifest is null)
        {
            throw new InvalidDataException(
                "Package verification failed: " + string.Join("; ", verification.Errors));
        }

        var stagingRoot = Path.GetFullPath(stagingDirectory);
        if (string.Equals(Path.GetPathRoot(stagingRoot), stagingRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Staging directory cannot be a volume root.", nameof(stagingDirectory));
        }

        if (Directory.Exists(stagingRoot) || File.Exists(stagingRoot))
        {
            throw new IOException($"Staging directory already exists: {stagingRoot}");
        }

        var parent = Path.GetDirectoryName(stagingRoot)
                     ?? throw new ArgumentException("Staging directory has no parent.", nameof(stagingDirectory));
        Directory.CreateDirectory(parent);
        var temporaryRoot = Path.Combine(parent, $".offline-package-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            using var archive = ZipFile.OpenRead(packageZipPath);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                                !string.Equals(entry.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

            foreach (var file in verification.Manifest.Files)
            {
                if (!TryNormalizeRelativePath(file.RelativePath, out var normalized, out var error))
                {
                    throw new InvalidDataException($"Invalid manifest path: {error}");
                }

                if (!entries.TryGetValue(normalized, out var entry))
                {
                    throw new InvalidDataException($"Missing archive entry: {normalized}");
                }

                var destinationPath = Path.GetFullPath(
                    Path.Combine(temporaryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!destinationPath.StartsWith(
                        temporaryRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Archive path escapes staging root: {normalized}");
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                entry.ExtractToFile(destinationPath, overwrite: false);
            }

            Directory.Move(temporaryRoot, stagingRoot);
            return verification.Manifest;
        }
        catch
        {
            TryDeleteDirectory(temporaryRoot);
            throw;
        }
    }

    private static bool TryNormalizeRelativePath(
        string? path,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        var candidate = path.Replace('\\', '/').Trim();
        if (candidate.StartsWith('/') || candidate.Contains(':') || candidate.Contains('\0'))
        {
            error = "Path contains an absolute prefix or invalid character.";
            return false;
        }

        var segments = candidate.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            error = "Path contains an empty segment, '.' or '..'.";
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T value)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
