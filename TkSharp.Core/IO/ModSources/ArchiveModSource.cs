using System.Runtime.CompilerServices;
using SharpCompress.Archives;

namespace TkSharp.Core.IO.ModSources;

public sealed class ArchiveModSource(IArchive archive, string? rootFolderPath) : TkModSourceBase<IArchiveEntry>(rootFolderPath)
{
    private readonly IArchive _archive = archive;
    private readonly string? _rootFolderPath = rootFolderPath;

    protected override IEnumerable<IArchiveEntry> Files => _archive.Entries
        .Where(entry => !entry.IsDirectory && (_rootFolderPath is null || entry.Key?.StartsWith(_rootFolderPath) is true));

    protected override Stream OpenRead(IArchiveEntry input)
    {
        lock (_archive) {
            MemoryStream copy = new();
            using var archive = input.OpenEntryStream();
            archive.CopyTo(copy);
            copy.Seek(0, SeekOrigin.Begin);
            return copy;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override string GetFileName(IArchiveEntry input)
    {
        return input.Key!.Replace('\\', '/');
    }
}