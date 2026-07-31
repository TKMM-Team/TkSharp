using TkSharp.Core;

namespace TkSharp.IO;

public sealed class TkSystemSource(string rootFolderPath) : ITkSystemSource
{
    private readonly Lock _bucketsLock = new();
    private string[]? _romfsBuckets;

    public Stream OpenRead(string relativeFilePath)
    {
        if (!TryResolvePath(relativeFilePath, out var resolved)) {
            throw new FileNotFoundException(
                $"Could not find '{relativeFilePath}' under '{rootFolderPath}'.",
                Path.Combine(rootFolderPath, relativeFilePath));
        }

        return File.OpenRead(resolved);
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

        var relative = normalized["romfs/".Length..];
        var relativeOs = relative.Replace('/', Path.DirectorySeparatorChar);
        var romfsRoot = Path.Combine(rootFolderPath, "romfs");
        if (!Directory.Exists(romfsRoot)) {
            resolvedPath = direct;
            return false;
        }

        foreach (var bucket in GetRomfsBuckets()) {
            var candidate = Path.Combine(bucket, relativeOs);
            if (File.Exists(candidate)) {
                resolvedPath = candidate;
                return true;
            }
        }

        var fileName = Path.GetFileName(relativeOs);
        if (fileName.Length > 0) {
            foreach (var file in Directory.EnumerateFiles(romfsRoot, fileName, SearchOption.AllDirectories)) {
                var underRomfs = Path.GetRelativePath(romfsRoot, file).Replace('\\', '/');
                
                if (!StripBucketPrefixes(underRomfs).Equals(relative, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                
                resolvedPath = file;
                return true;
            }
        }

        resolvedPath = direct;
        return false;
    }

    private static string StripBucketPrefixes(string path)
    {
        while (path.StartsWith("TKMM", StringComparison.OrdinalIgnoreCase)) {
            var i = 4;
            while (i < path.Length && char.IsDigit(path[i])) {
                i++;
            }

            if (i == 4 || i >= path.Length || path[i] != '/') {
                break;
            }

            path = path[(i + 1)..];
        }

        return path;
    }

    private string[] GetRomfsBuckets()
    {
        lock (_bucketsLock) {
            if (_romfsBuckets is not null) {
                return _romfsBuckets;
            }

            var romfsRoot = Path.Combine(rootFolderPath, "romfs");
            if (!Directory.Exists(romfsRoot)) {
                return _romfsBuckets = [];
            }

            return _romfsBuckets = Directory.EnumerateDirectories(romfsRoot)
                .Where(static path => {
                    var name = Path.GetFileName(path.AsSpan());
                    if (name.Length < 5 || !name.StartsWith("TKMM", StringComparison.OrdinalIgnoreCase)) {
                        return false;
                    }

                    for (var i = 4; i < name.Length; i++) {
                        if (!char.IsDigit(name[i])) {
                            return false;
                        }
                    }

                    return name.Length > 4;
                })
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}