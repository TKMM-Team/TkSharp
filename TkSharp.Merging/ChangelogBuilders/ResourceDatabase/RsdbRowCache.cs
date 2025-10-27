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
        if (!_overflow.TryGetValue(dbNameHash, out var rows) ||
            !rows.TryGetValue(rowId, out var result)) {
            return null;
        }

        var entry = result[0];

        for (var i = 1; i < result.Length; i++) {
            var next = result[i];
            if (next.Version > dbFileVersion) {
                break;
            }

            entry = next;
        }

        return entry.Row;
    }

    static RsdbRowCache()
    {
        using var stream = typeof(RsdbRowCache).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.RsdbCache.bpcc")!;

        var count = stream.Read<int>();
        MutableOverflowMap overflow = new(count);

        for (var i = 0; i < count; i++) {
            var hash = stream.Read<ulong>();
            var entryCount = stream.Read<int>();
            overflow.Add(hash,
                ReadEntries(stream, entryCount)
            );
        }

        _overflow = overflow.ToFrozenDictionary();
    }

    private static OverflowMapEntries ReadEntries(Stream stream, int count)
    {
        MutableOverflowMapEntries entries = [];

        for (var i = 0; i < count; i++) {
            var rowId = stream.Read<ulong>();
            var versionCount = stream.Read<int>();
            entries.Add(rowId, ReadVersionEntries(stream, versionCount));
        }

        return entries.ToFrozenDictionary();
    }

    private static OverflowMapEntry[] ReadVersionEntries(Stream stream, int count)
    {
        var entries = new OverflowMapEntry[count];

        for (var i = 0; i < count; i++) {
            var version = stream.Read<int>();
            var bymlBufferSize = stream.Read<int>();

            using var buffer = SpanOwner<byte>.Allocate(bymlBufferSize);
            var read = stream.Read(buffer.Span);
            Debug.Assert(read == buffer.Length);

            entries[i] = (
                Row: Byml.FromBinary(buffer.Span),
                Version: version
            );
        }

        return entries;
    }
}