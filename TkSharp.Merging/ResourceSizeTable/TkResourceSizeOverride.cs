using RstbLibrary;
using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp.Merging.ResourceSizeTable;

public static class TkResourceSizeOverride
{
    public const string CANONICAL = "System/Resource/ResourceSizeTable.Product.rsizetable";

    public static void Write(
        ITkModWriter writer,
        TkChangelog changelog,
        int gameVersion,
        bool applyToAllVersions,
        IEnumerable<KeyValuePair<string, uint>> entries)
    {
        Rstb table = new();
        foreach (var (canonical, size) in entries) {
            table.OverflowTable[canonical] = size;
        }

        if (table.OverflowTable.Count == 0) {
            return;
        }

        TkChangelogEntry entry = new(
            CANONICAL,
            ChangelogEntryType.Changelog,
            TkFileAttributes.IsProductFile | TkFileAttributes.HasZsExtension,
            zsDictionaryId: -1,
            versions: applyToAllVersions ? [] : [gameVersion]);
        changelog.ChangelogFiles.Add(entry);

        using var output = writer.OpenWrite(GetRelativePath(gameVersion, applyToAllVersions));
        table.WriteBinary(output);
    }

    public static bool TryGetValue(Rstb table, string canonical, out uint size)
    {
        return table.OverflowTable.TryGetValue(canonical, out size);
    }

    public static string GetRelativePath(int gameVersion, bool applyToAllVersions = false)
    {
        return Path.Combine("romfs", applyToAllVersions ? CANONICAL : $"{CANONICAL}{gameVersion}");
    }
}