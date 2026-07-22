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

        var relativePath = applyToAllVersions ? GetRelativePath() : GetRelativePath(gameVersion);
        using var output = writer.OpenWrite(relativePath);
        table.WriteBinary(output);
    }

    public static string GetRelativePath()
    {
        return Path.Combine("romfs", CANONICAL);
    }

    public static string GetRelativePath(int gameVersion)
    {
        return Path.Combine("romfs", $"{CANONICAL}{gameVersion}");
    }
}