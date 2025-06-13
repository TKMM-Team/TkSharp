using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BymlLibrary;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using MutableOverflowMap = System.Collections.Generic.Dictionary<ulong, System.Collections.Frozen.FrozenDictionary<ulong, (BymlLibrary.Byml Row, int Version)[]>>;
using MutableOverflowMapEntries = System.Collections.Generic.Dictionary<ulong, (BymlLibrary.Byml Row, int Version)[]>;
using OverflowMap = System.Collections.Frozen.FrozenDictionary<ulong, System.Collections.Frozen.FrozenDictionary<ulong, (BymlLibrary.Byml Row, int Version)[]>>;
using OverflowMapEntries = System.Collections.Frozen.FrozenDictionary<ulong, (BymlLibrary.Byml Row, int Version)[]>;
using OverflowMapEntry = (BymlLibrary.Byml Row, int Version);

namespace TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

public static class RsdbRowCache
{
    private static readonly OverflowMap _overflow;
    
    public static bool TryGetVanilla(ulong dbNameHash, ulong rowId, int dbFileVersion, [MaybeNullWhen(false)] out Byml vanilla)
    {
        vanilla = GetVanilla(dbNameHash, rowId, dbFileVersion);
        return vanilla is not null;
    }

    public static Byml? GetVanilla(ulong dbNameHash, ulong rowId, int dbFileVersion)
    {
        if (!_overflow[dbNameHash].TryGetValue(rowId, out OverflowMapEntry[]? result)) {
            return null;
        }

        OverflowMapEntry entry = result[0];

        for (int i = 1; i < result.Length; i++) {
            OverflowMapEntry next = result[i];
            if (next.Version > dbFileVersion) {
                break;
            }

            entry = next;
        }

        return entry.Row;
    }

    static RsdbRowCache()
    {
        using Stream stream = typeof(RsdbRowCache).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.RsdbRowCache.bpcc")!;

        MutableOverflowMap overflow = [];

        int count = stream.Read<int>();
        for (int i = 0; i < count; i++) {
            ulong hash = stream.Read<ulong>();
            int entryCount = stream.Read<int>();
            overflow.Add(hash,
                ReadEntries(stream, entryCount)
            );
        }

        _overflow = overflow.ToFrozenDictionary();
    }

    private static OverflowMapEntries ReadEntries(Stream stream, int count)
    {
        MutableOverflowMapEntries entries = [];

        for (int i = 0; i < count; i++) {
            ulong rowId = stream.Read<ulong>();
            int versionCount = stream.Read<int>();
            entries.Add(rowId, ReadVersionEntries(stream, versionCount));
        }

        return entries.ToFrozenDictionary();
    }

    private static OverflowMapEntry[] ReadVersionEntries(Stream stream, int count)
    {
        var entries = new OverflowMapEntry[count];

        for (int i = 0; i < count; i++) {
            int version = stream.Read<int>();
            int bymlBufferSize = stream.Read<int>();

            using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(bymlBufferSize);
            int read = stream.Read(buffer.Span);
            Debug.Assert(read == buffer.Length);

            entries[i] = (
                Row: Byml.FromBinary(buffer.Span),
                Version: version
            );
        }

        return entries;
    }
}