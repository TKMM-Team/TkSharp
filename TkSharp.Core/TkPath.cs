using TkSharp.Core.Extensions;

namespace TkSharp.Core;

public readonly ref struct TkPath(ReadOnlySpan<char> canonical, int fileVersion, TkFileAttributes attributes, ReadOnlySpan<char> root, string originPath)
{
    public readonly ReadOnlySpan<char> Canonical = canonical;

    public readonly int FileVersion = fileVersion;

    public readonly TkFileAttributes Attributes = attributes;

    public readonly ReadOnlySpan<char> Root = root;

    public readonly ReadOnlySpan<char> Extension = Path.GetExtension(canonical);

    public readonly string OriginPath = originPath;

    /// <summary>
    /// Parse a path and root into a <see cref="TkPath"/> struct.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="rootFolderPath"></param>
    /// <param name="isInvalid"></param>
    /// <returns></returns>
    public static TkPath FromPath(string path, string rootFolderPath, out bool isInvalid)
    {
        if (rootFolderPath.Length >= path.Length) {
            throw new ArgumentException(
                "The root of the path must be longer than the path itself.", nameof(rootFolderPath)
            );
        }
        
        isInvalid = false;

        int rootFolderLength = rootFolderPath is "" ? 0 : rootFolderPath[^1] switch {
            '/' or '\\' => rootFolderPath.Length,
            _ => rootFolderPath.Length + 1
        };

        var span = path.AsSpan();
        var relative = span[rootFolderLength..];

        if (relative.Length < 6) {
            throw new ArgumentException(
                "The path must contain a root folder or 'romfs', 'exefs' or 'cheats'.", nameof(path)
            );
        }

        Span<char> root = stackalloc char[6];
        relative[..6].ToLowerInvariant(root);

        int rootLength = root[..5] switch {
            "romfs" or "exefs" => 5,
            "cheat" or "extra" when root[^1] is 's' => 6,
            _ => -1
        };

        if (rootLength == -1) {
            isInvalid = true;
            return default;
        }

        var canonical = span[(rootFolderLength + rootLength + 1)..].GetCanonical(
            out int fileVersion, out var attributes
        );

        return new TkPath(
            canonical,
            fileVersion,
            attributes,
            relative[..rootLength],
            path
        );
    }
}