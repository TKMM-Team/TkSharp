using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using TkSharp.Merging.ChangelogBuilders;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public class BymlMergeTracking(string canonical) : Dictionary<BymlArray, BymlMergeTrackingEntry>
{
    private readonly string _canonical = canonical;

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
    }

    private void ApplyEntry(BymlArray @base, BymlMergeTrackingEntry entry, ref BymlTrackingInfo info)
    {
        info.Depth = entry.Depth;

        int newEntryOffset = 0;

        foreach (int i in entry.Removals) {
            @base[i] = BymlChangeType.Remove;
        }

        IEnumerable<(int Key, Byml[])> additions = entry.Additions
            .SelectMany(x => x)
            .GroupBy(x => x.InsertIndex, x => x.Entry)
            .OrderBy(x => x.Key)
            .Select(x => (x.Key, x.ToArray()));

        Dictionary<Byml, int> keyedAdditions = new(Byml.ValueEqualityComparer.Default);

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

    private void ProcessAdditions(ref int newEntryOffset, BymlArray @base, BymlMergeTrackingEntry entry, int insertIndex,
        Byml[] additions, ref BymlTrackingInfo info, Dictionary<Byml, int> keyedAdditions)
    {
        if (additions.Length == 0) {
            return;
        }

        if (entry.ArrayName is string arrayName &&
            BymlMergerKeyNameProvider.Instance.GetKeyName(arrayName, Type ?? info.Type, info.Depth) is string keyName) {
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
        string keyName, ref BymlTrackingInfo info, Dictionary<Byml, int> keyedAdditions)
    {
        IEnumerable<(Byml? Key, Byml[])> elements = additions
            .GroupBy(x => (x.Value as BymlMap)?.GetValueOrDefault(keyName), Byml.ValueEqualityComparer.Default)
            .Select(x => (x.Key, x.ToArray()));

        foreach ((Byml? key, Byml[] entries) in elements) {
            if (entries.Length == 0) {
                continue;
            }

            if (key is null) {
                InsertAdditions(ref newEntryOffset, @base, insertIndex, entries);
                continue;
            }

            if (keyedAdditions.TryGetValue(key, out int oldIndex)) {
                ref Byml existingEntry = ref entries[oldIndex];
                MergeKeyedAdditions(existingEntry, entries, ref newEntryOffset, @base, insertIndex, ref info);
                existingEntry = BymlChangeType.Remove;
                return;
            }

            keyedAdditions.Add(key, insertIndex);

            if (entries.Length == 1) {
                InsertAddition(ref newEntryOffset, @base, insertIndex, entries[0]);
                continue;
            }

            MergeKeyedAdditions(entries[0], entries.AsSpan(1..), ref newEntryOffset, @base, insertIndex, ref info);
        }
    }

    private void MergeKeyedAdditions(Byml @base, Span<Byml> entries, ref int newEntryOffset, BymlArray baseArray, int insertIndex, ref BymlTrackingInfo info)
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
        InsertAddition(ref newEntryOffset, baseArray, insertIndex, @base);
    }

    private static void InsertAdditions(ref int newEntryOffset, BymlArray @base, int insertIndex, Byml[] additions)
    {
        foreach (Byml addition in additions) {
            InsertAddition(ref newEntryOffset, @base, insertIndex, addition);
        }
    }

    private static void InsertAddition(ref int newEntryOffset, BymlArray @base, int insertIndex, Byml addition)
    {
        int relativeIndex = insertIndex + newEntryOffset;

        if (@base.Count > relativeIndex) {
            @base.Insert(insertIndex + newEntryOffset, addition);
        }
        else {
            @base.Add(addition);
        }

        newEntryOffset++;
    }

    private ReadOnlySpan<char> GetBgymlType()
    {
        ReadOnlySpan<char> result = Path.GetExtension(
            Path.GetFileNameWithoutExtension(_canonical.AsSpan())
        );

        return result.IsEmpty ? default : result[1..];
    }
}