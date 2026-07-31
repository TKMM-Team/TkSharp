namespace TkSharp.Core.IO;

/// <summary>
/// Remaps <c>romfs/...</c> writes into <c>romfs/TKMM{n}/...</c>,
/// rotating buckets every <see cref="MAX_FILES_PER_BUCKET"/> files for FAT32 limits.
/// </summary>
public sealed class RomfsBucketModWriter(ITkModWriter inner) : ITkModWriter
{
    private const int MAX_FILES_PER_BUCKET = 3000;

    private readonly Lock _lock = new();
    private readonly HashSet<string> _writtenInCurrentBucket = new(StringComparer.OrdinalIgnoreCase);
    private int _bucketIndex = 1;

    public Stream OpenWrite(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');

        if (!normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)) {
            return inner.OpenWrite(filePath);
        }

        var relative = normalized["romfs/".Length..];
        string remapped;

        lock (_lock) {
            if (_writtenInCurrentBucket.Count >= MAX_FILES_PER_BUCKET
                && !_writtenInCurrentBucket.Contains(relative)) {
                _bucketIndex++;
                _writtenInCurrentBucket.Clear();
            }

            _writtenInCurrentBucket.Add(relative);
            remapped = Path.Combine("romfs", $"TKMM{_bucketIndex:D3}", relative.Replace('/', Path.DirectorySeparatorChar));
        }

        return inner.OpenWrite(remapped);
    }

    public void SetRelativeFolder(string rootFolder)
    {
        lock (_lock) {
            _bucketIndex = 1;
            _writtenInCurrentBucket.Clear();
        }

        inner.SetRelativeFolder(rootFolder);
    }
}
