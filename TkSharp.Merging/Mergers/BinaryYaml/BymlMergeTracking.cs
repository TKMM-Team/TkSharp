using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance;
using TkSharp.Merging.ChangelogBuilders;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.Common.BinaryYaml;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public class BymlMergeTracking(string canonical)
{
    private readonly string _canonical = canonical;
    private readonly Dictionary<IEnumerable, object> _maps = [];
    
    public Dictionary<BymlArray, BymlMergeTrackingArrayEntry> Arrays { get; } = [];
    

    public int Depth { get; set; }

    public string? Type { get; set; }

    public void Apply()
    {
        ReadOnlySpan<char> type = GetBgymlType();
        BymlTrackingInfo info = new() {
            Type = type
        };

        foreach ((var map, object entry) in _maps) {
            switch (map) {
                case IDictionary<uint, Byml> hashMap32:
                    ApplyMapEntry(hashMap32, entry);
                    continue;
                case IDictionary<ulong, Byml> hashMap64:
                    ApplyMapEntry(hashMap64, entry);
                    continue;
                default:
                    ApplyMapEntry((IDictionary<string, Byml>)map, entry);
                    continue;
            }
        }

        foreach (var (array, entry) in Arrays) {
            ApplyArrayEntry(array, entry, ref info);
        }
    }

    public BymlMergeTrackingMapEntry<TKey> GetMapTrackingEntry<TKey>(ICollection<KeyValuePair<TKey, Byml>> @base) where TKey : notnull
    {
        ref object? entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_maps, @base, out bool hasEntry);
        if (!hasEntry || Unsafe.IsNullRef(ref entry) || entry is null) {
            entry = new BymlMergeTrackingMapEntry<TKey>();
        }

        return (BymlMergeTrackingMapEntry<TKey>)entry;
    }

    public void RemoveMapTrackingEntry<TKey>(ICollection<KeyValuePair<TKey, Byml>> @base, TKey key) where TKey : notnull
    {
        if (!_maps.TryGetValue(@base, out object? entry) || entry is not BymlMergeTrackingMapEntry<TKey> mapTrackingEntry) {
            return;
        }

        mapTrackingEntry.Remove(key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyMapEntry<TKey>(IDictionary<TKey, Byml> @base, object entry) where TKey : notnull
        => ApplyMapEntry(@base, (BymlMergeTrackingMapEntry<TKey>)entry);

    private static void ApplyMapEntry<TKey>(IDictionary<TKey, Byml> @base, BymlMergeTrackingMapEntry<TKey> entry) where TKey : notnull
    {
        foreach (var key in entry) {
            @base.Remove(key);
        }
    }
    
    private void ApplyArrayEntry(BymlArray @base, BymlMergeTrackingArrayEntry entry, ref BymlTrackingInfo info)
    {
        info.Depth = entry.Depth;

        int newEntryOffset = 0;

        foreach (int i in entry.Removals.Where(i => @base.Count > i)) {
            @base[i] = BymlChangeType.Remove;
        }
        
        foreach ((_, int i) in entry.KeyedRemovals.Where(i => @base.Count > i.Value)) {
            @base[i] = BymlChangeType.Remove;
        }

        IEnumerable<(int Key, Byml[])> additions = entry.Additions
            .SelectMany(x => x)
            .GroupBy(x => x.InsertIndex, x => x.Entry)
            .OrderBy(x => x.Key)
            .Select(x => (x.Key, x.ToArray()));

        Dictionary<BymlKey, int> keyedAdditions = new();

        foreach ((int insertIndex, Byml[] entries) in additions) {
            ProcessAdditions(ref newEntryOffset, @base, entry, insertIndex, entries, ref info, keyedAdditions);
        }

        for (int i = 0; i < @base.Count; i++) {
            if (@base[i].Value is not BymlChangeType.Remove) {
                continue;
            }

            @base.RemoveAt(i);
            i--;
        }
    }

    private void ProcessAdditions(ref int newEntryOffset, BymlArray @base, BymlMergeTrackingArrayEntry entry, int insertIndex,
        Byml[] additions, ref BymlTrackingInfo info, Dictionary<BymlKey, int> keyedAdditions)
    {
        if (additions.Length == 0) {
            return;
        }

        if (entry.ArrayName is string arrayName &&
            BymlMergerKeyNameProvider.Instance.GetKeyName(arrayName, Type ?? info.Type, info.Depth) is var keyName) {
            ProcessKeyedAdditions(ref newEntryOffset, @base, insertIndex, additions, keyName, ref info, keyedAdditions);
            return;
        }
        
        if (additions.Length == 1) {
            InsertAddition(ref newEntryOffset, @base, insertIndex, additions[0]);
            return;
        }

        InsertAdditions(ref newEntryOffset, @base, insertIndex, additions);
    }

    private void ProcessKeyedAdditions(ref int newEntryOffset, BymlArray @base, int insertIndex, Byml[] additions,
        BymlKeyName keyName, ref BymlTrackingInfo info, Dictionary<BymlKey, int> keyedAdditions)
    {
        IEnumerable<(BymlKey Key, Byml[])> elements = additions
            .GroupBy(keyName.GetKey)
            .Select(x => (x.Key, x.ToArray()));

        foreach ((BymlKey key, Byml[] entries) in elements) {
            if (entries.Length == 0) {
                continue;
            }

            if (key.IsEmpty) {
                InsertAdditions(ref newEntryOffset, @base, insertIndex, entries);
                continue;
            }

            if (keyedAdditions.TryGetValue(key, out int oldIndex)) {
                ref Byml existingEntry = ref @base.AsSpan()[oldIndex];
                int index = MergeKeyedAdditions(existingEntry, entries, ref newEntryOffset, @base, insertIndex, ref info);
                existingEntry = BymlChangeType.Remove;
                keyedAdditions[key] = index;
                continue;
            }

            int insertResult;
            if (entries.Length == 1) {
                insertResult = InsertAddition(ref newEntryOffset, @base, insertIndex, entries[0]);
                goto UpdateKeys;
            }

            insertResult = MergeKeyedAdditions(entries[0], entries.AsSpan(1..), ref newEntryOffset, @base, insertIndex, ref info);
            
        UpdateKeys:
            keyedAdditions.Add(key, insertResult);
        }
    }

    private int MergeKeyedAdditions(Byml @base, Span<Byml> entries, ref int newEntryOffset, BymlArray baseArray, int insertIndex, ref BymlTrackingInfo info)
    {
        for (int i = 0; i < entries.Length; i++) {
            BymlChangelogBuilder.LogChangesInline(ref info, ref entries[i], @base);
        }

        // This is as sketchy as it looks
        BymlMergeTracking tracking = new(_canonical);

        foreach (Byml changelog in entries) {
            BymlMerger.Merge(@base, changelog, tracking);
        }

        tracking.Apply();
        return InsertAddition(ref newEntryOffset, baseArray, insertIndex, @base);
    }

    private static void InsertAdditions(ref int newEntryOffset, BymlArray @base, int insertIndex, Byml[] additions)
    {
        foreach (Byml addition in additions) {
            InsertAddition(ref newEntryOffset, @base, insertIndex, addition);
        }
    }

    private static int InsertAddition(ref int newEntryOffset, BymlArray @base, int insertIndex, Byml addition)
    {
        int relativeIndex = insertIndex + newEntryOffset;
        newEntryOffset++;

        if (@base.Count > relativeIndex) {
            @base.Insert(relativeIndex, addition);
            return relativeIndex;
        }

        @base.Add(addition);
        return @base.Count - 1;
    }

    private ReadOnlySpan<char> GetBgymlType()
    {
        ReadOnlySpan<char> result = Path.GetExtension(
            Path.GetFileNameWithoutExtension(_canonical.AsSpan())
        );

        return result.IsEmpty ? default : result[1..];
    }
}