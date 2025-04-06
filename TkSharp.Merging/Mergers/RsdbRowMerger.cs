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

    public void Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Byml merged = Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version);
        BymlArray rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (RentedBuffers<byte>.Entry input in inputs) {
            MergeEntry(rows, input.Span, tracking);
        }

        tracking.Apply();

        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
    }

    public void Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Byml merged = Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version);
        BymlArray rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (ArraySegment<byte> input in inputs) {
            MergeEntry(rows, input, tracking);
        }

        tracking.Apply();

        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
    }

    public void MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        Byml merged = Byml.FromBinary(@base, out Endianness endianness, out ushort version);
        BymlArray rows = merged.GetArray();
        BymlMergeTracking tracking = new(entry.Canonical);
        MergeEntry(rows, input, tracking);
        tracking.Apply();
        rows.Sort(_rowComparer);
        merged.WriteBinary(output, endianness, version);
    }

    private void MergeEntry(BymlArray rows, Span<byte> input, BymlMergeTracking tracking)
    {
        Byml changelog = Byml.FromBinary(input);

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
        for (int i = 0; i < @base.Count; i++) {
            Byml entry = @base[i];
            BymlMap baseMap = entry.GetMap();
            var key = baseMap[_keyName].Get<TKey>();

            if (!changelog.Remove(key, out Byml? changelogEntry)) {
                continue;
            }
            
            if (!tracking.TryGetValue(@base, out BymlMergeTrackingEntry? trackingEntry)) {
                tracking[@base] = trackingEntry = new BymlMergeTrackingEntry();
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

        foreach ((_, Byml value) in changelog) {   
            @base.Add(value);
        }
    }
}