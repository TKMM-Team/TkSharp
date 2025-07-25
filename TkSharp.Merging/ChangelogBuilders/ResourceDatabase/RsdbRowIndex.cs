using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

namespace TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

public static class RsdbRowIndex
{
    private static readonly FrozenDictionary<ulong, FrozenDictionary<ulong, int>> _lookup;

    static RsdbRowIndex()
    {
        using Stream stream = typeof(RsdbRowIndex).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.RsdbIndex.bpcc")!;

        Dictionary<ulong, FrozenDictionary<ulong, int>> lookup = [];

        int count = stream.Read<int>();
        for (int i = 0; i < count; i++) {
            ulong hash = stream.Read<ulong>();
            int entryCount = stream.Read<int>();
            lookup.Add(hash,
                ReadEntries(stream, entryCount)
            );
        }

        _lookup = lookup.ToFrozenDictionary();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetIndex(ulong dbNameHash, ulong rowId, out int index)
    {
        return _lookup[dbNameHash].TryGetValue(rowId, out index);
    }
    
    private static FrozenDictionary<ulong, int> ReadEntries(Stream stream, int count)
    {
        Dictionary<ulong, int> entries = [];
        for (int i = 0; i < count; i++) {
            entries.Add(stream.Read<ulong>(), stream.Read<int>());
        }

        return entries.ToFrozenDictionary();
    }
}