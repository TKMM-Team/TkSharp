using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance;
using TkSharp.Merging.ChangelogBuilders;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.Common.BinaryYaml;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public class BymlMergeTracking(string canonical) : Dictionary<BymlArray, BymlMergeTrackingEntry>
{
    private readonly string _canonical = canonical;
    private readonly Dictionary<object, BymlMergeTrackingEntry> _mapTracking = new();

    public int Depth { get; set; }

    public string? Type { get; set; }

    public void Apply()
    {
        ReadOnlySpan<char> type = GetBgymlType();
        BymlTrackingInfo info = new() {
            Type = type
        };

        foreach ((BymlArray @base, BymlMergeTrackingEntry entry) in this) {
            ApplyEntry(@base, entry, ref info);
        }

        foreach (var (map, entry) in _mapTracking) {
            switch (map) {
                case IDictionary<string, Byml> stringMap:
                    ApplyMapEntry(stringMap, entry, ref info);
                    break;
                case IDictionary<uint, Byml> uintMap:
                    ApplyMapEntry(uintMap, entry, ref info);
                    break;
                case IDictionary<ulong, Byml> ulongMap:
                    ApplyMapEntry(ulongMap, entry, ref info);
                    break;
            }
        }
    }

    private void ApplyEntry(BymlArray @base, BymlMergeTrackingEntry entry, ref BymlTrackingInfo info)
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

    private static void ApplyMapEntry<T>(IDictionary<T, Byml> @base, BymlMergeTrackingEntry entry, ref BymlTrackingInfo info)
    {
        info.Depth = entry.Depth;

        foreach (var key in entry.MapRemovals) {
            if (key is T typedKey) {
                @base.Remove(typedKey);
            }
        }
    }

    public BymlMergeTrackingEntry GetOrCreateMapEntry(object map)
    {
        if (!_mapTracking.TryGetValue(map, out var entry)) {
            _mapTracking[map] = entry = new BymlMergeTrackingEntry { Depth = Depth };
        }
        return entry;
    }

    private void ProcessAdditions(ref int newEntryOffset, BymlArray @base, BymlMergeTrackingEntry entry, int insertIndex,
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