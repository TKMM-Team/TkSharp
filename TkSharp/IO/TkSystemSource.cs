using TkSharp.Core;

namespace TkSharp.IO;

public sealed class TkSystemSource(string rootFolderPath) : ITkSystemSource
{
    private string[]? _romfsBuckets;

    public Stream OpenRead(string relativeFilePath)
    {
        return File.OpenRead(ResolvePath(relativeFilePath));
    }

    public bool Exists(string relativeFilePath)
    {
        return TryResolvePath(relativeFilePath, out _);
    }

    public ITkSystemSource GetRelative(string relativeSourcePath)
    {
        return new TkSystemSource(
            Path.Combine(rootFolderPath, relativeSourcePath)
        );
    }

    public IEnumerable<string> EnumerateRomfsBuckets()
    {
        foreach (var bucket in GetRomfsBuckets()) {
            yield return Path.GetFileName(bucket);
        }
    }

    private string ResolvePath(string relativeFilePath)
    {
        if (TryResolvePath(relativeFilePath, out var resolved)) {
            return resolved;
        }

        return Path.Combine(rootFolderPath, relativeFilePath);
    }

    private bool TryResolvePath(string relativeFilePath, out string resolvedPath)
    {
        var direct = Path.Combine(rootFolderPath, relativeFilePath);
        if (File.Exists(direct)) {
            resolvedPath = direct;
            return true;
        }

        var normalized = relativeFilePath.Replace('\\', '/');
        if (!normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)) {
            resolvedPath = direct;
            return false;
        }

        var relative = normalized["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar);
        foreach (var bucket in GetRomfsBuckets()) {
            var candidate = Path.Combine(bucket, relative);
            if (File.Exists(candidate)) {
                resolvedPath = candidate;
                return true;
            }
        }

        resolvedPath = direct;
        return false;
    }

    private string[] GetRomfsBuckets()
    {
        if (_romfsBuckets is not null) {
            return _romfsBuckets;
        }

        var romfsRoot = Path.Combine(rootFolderPath, "romfs");
        if (!Directory.Exists(romfsRoot)) {
            return _romfsBuckets = [];
        }

        return _romfsBuckets = Directory.GetDirectories(romfsRoot, "TKMM*")
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}