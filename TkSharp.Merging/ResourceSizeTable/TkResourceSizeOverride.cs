using RstbLibrary;
using RstbLibrary.Helpers;
using TkSharp.Core.Models;

namespace TkSharp.Merging.ResourceSizeTable;

public static class TkResourceSizeOverride
{
    private const string PREFIX = "RSTB:";

    public static uint Resolve(Rstb vanilla, TkChangelog changelog, string canonical)
    {
        if (!canonical.EndsWith(".bfres", StringComparison.Ordinal)) {
            return 0;
        }

        var prefix = $"{PREFIX}{canonical}:";
        var value = changelog.Reserved1.LastOrDefault(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
        if (value is null || !uint.TryParse(value.AsSpan(prefix.Length), out var authoredSize)) {
            return 0;
        }

        return TryGetResourceSize(vanilla, canonical, out var vanillaSize) && authoredSize == vanillaSize ? 0 : authoredSize;
    }

    public static void Collect(Rstb table, IEnumerable<TkChangelogEntry> entries, ICollection<string> output)
    {
        foreach (var entry in entries) {
            if (!entry.Canonical.EndsWith(".bfres", StringComparison.Ordinal)
                || !TryGetResourceSize(table, entry.Canonical, out var size)) {
                continue;
            }

            output.Add($"{PREFIX}{entry.Canonical}:{size}");
        }
    }

    private static bool TryGetResourceSize(Rstb table, string canonical, out uint size)
    {
        return table.OverflowTable.TryGetValue(canonical, out size) || table.HashTable.TryGetValue(Crc32.Compute(canonical), out size);
    }
}