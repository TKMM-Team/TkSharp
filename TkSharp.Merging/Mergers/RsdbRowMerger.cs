using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Microsoft.Extensions.Logging;
using Revrs;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers.BinaryYaml;

namespace TkSharp.Merging.Mergers;

public static class RsdbRowMergers
{
    public static readonly RsdbRowMerger RowId = new("__RowId");
    public static readonly RsdbRowMerger Name = new("Name");
    public static readonly RsdbRowMerger FullTagId = new("FullTagId");
    public static readonly RsdbRowMerger NameHash = new("NameHash");
}

public sealed class RsdbRowMerger(string keyName) : ITkMerger
{
    private readonly string _keyName = keyName;
    private readonly BymlRowComparer _rowComparer = new(keyName);

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Byml.FromBinary(vanillaData, out var endianness, out var version);
        var rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (var input in inputs) {
            MergeEntry(rows, input.Span, tracking);
        }

        tracking.Apply();

        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Byml.FromBinary(vanillaData, out var endianness, out var version);
        var rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (var input in inputs) {
            MergeEntry(rows, input, tracking);
        }

        tracking.Apply();

        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        var merged = Byml.FromBinary(@base, out var endianness, out var version);
        var rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);
        MergeEntry(rows, input, tracking);
        tracking.Apply();
        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    private void MergeEntry(BymlArray rows, Span<byte> input, BymlMergeTracking tracking)
    {
        var changelog = Byml.FromBinary(input);

        switch (changelog.Value) {
            case IDictionary<uint, Byml> hashMap32:
                MergeMap(hashMap32, rows, tracking);
                break;
            case IDictionary<string, Byml> map:
                MergeMap(map, rows, tracking);
                break;
        }
    }

    private void MergeMap<TKey>(IDictionary<TKey, Byml> changelog, BymlArray @base, BymlMergeTracking tracking) where TKey : notnull
    {
        for (var i = 0; i < @base.Count; i++) {
            var entry = @base[i];
            var baseMap = entry.GetMap();
            var key = baseMap[_keyName].Get<TKey>();

            if (!changelog.Remove(key, out var changelogEntry)) {
                continue;
            }
            
            if (!tracking.Arrays.TryGetValue(@base, out var trackingEntry)) {
                tracking.Arrays[@base] = trackingEntry = new BymlMergeTrackingArrayEntry();
            }

            if (changelogEntry.Value is BymlChangeType.Remove) {
                trackingEntry.KeyedRemovals[key] = i;
                continue;
            }

            if (trackingEntry.KeyedRemovals.Remove(key)) {
                TkLog.Instance.LogWarning(
                    "An RSDB entry with the key '{Key}' was removed by one mod and later modified. " +
                    "The entry has been re-added and may cause issues.", key);
            }

            BymlMerger.MergeMap(baseMap, changelogEntry.GetMap(), tracking);
        }

        foreach (var (_, value) in changelog) {   
            @base.Add(value);
        }
    }
}