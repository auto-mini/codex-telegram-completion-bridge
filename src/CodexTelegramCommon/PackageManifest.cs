using System.Text;

namespace CodexTelegramCommon;

public sealed record ManifestEntry(string RelativePath, string Sha256);

public sealed class PackageManifest(string packageRoot, IReadOnlyList<ManifestEntry> entries, string manifestSha256)
{
    public string PackageRoot { get; } = Path.GetFullPath(packageRoot);
    public IReadOnlyList<ManifestEntry> Entries { get; } = entries;
    public string ManifestSha256 { get; } = manifestSha256;
    public static PackageManifest LoadAndVerify(
        string packageRoot,
        bool allowInstalledMutableFiles = false)
    {
        var root = Path.GetFullPath(packageRoot);
        var manifestPath = Path.Combine(root, "manifest.sha256");
        var bytes = File.ReadAllBytes(manifestPath);
        var entries = Parse(bytes);
        var manifest = new PackageManifest(root, entries, Hashing.Sha256Hex(bytes));
        manifest.VerifyFiles(allowInstalledMutableFiles);
        return manifest;
    }

    public void VerifyFiles(bool allowInstalledMutableFiles = false)
    {
        foreach (var entry in Entries)
        {
            var path = ResolveContainedPath(entry.RelativePath);
            if (!File.Exists(path) || !string.Equals(Hashing.Sha256File(path), entry.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Package manifest verification failed for {entry.RelativePath}.");
            }
        }

        Require("bin/CodexTelegramBridge.exe");
        Require("bin/CodexTelegramCtl.exe");
        var listed = Entries.Select(entry => entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(PackageRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(PackageRoot, file).Replace('\\', '/');
            if (string.Equals(relative, "manifest.sha256", StringComparison.OrdinalIgnoreCase) || listed.Contains(relative))
            {
                continue;
            }

            if (!allowInstalledMutableFiles || !IsInstalledMutablePath(relative))
            {
                throw new InvalidDataException($"Unlisted package artifact: {relative}.");
            }
        }
    }

    public string ResolveContainedPath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(PackageRoot, normalized));
        var prefix = Path.TrimEndingDirectorySeparator(PackageRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Manifest path escapes the package root.");
        }

        return full;
    }

    private void Require(string relativePath)
    {
        if (!Entries.Any(entry => string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Package manifest is missing {relativePath}.");
        }
    }

    private static bool IsInstalledMutablePath(string relativePath)
    {
        var top = relativePath.Split('/')[0];
        return top.Equals("config", StringComparison.OrdinalIgnoreCase) ||
               top.Equals("state", StringComparison.OrdinalIgnoreCase) ||
               top.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
               top.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
               top.Equals("transaction-final.json", StringComparison.OrdinalIgnoreCase) ||
               top.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ManifestEntry> Parse(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Package manifest is not valid UTF-8.", exception);
        }

        var entries = new List<ManifestEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 67 || rawLine[64] != ' ' || rawLine[65] != ' ')
            {
                throw new InvalidDataException("Package manifest line is malformed.");
            }

            var hash = rawLine[..64];
            var relative = rawLine[66..].Replace('\\', '/');
            if (!hash.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(relative) ||
                Path.IsPathFullyQualified(relative) ||
                relative.Split('/').Any(component => component is "" or "." or "..") ||
                relative.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
                !seen.Add(relative))
            {
                throw new InvalidDataException("Package manifest entry is unsafe or duplicated.");
            }

            entries.Add(new ManifestEntry(relative, hash.ToLowerInvariant()));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("Package manifest is empty.");
        }

        return entries;
    }
}
