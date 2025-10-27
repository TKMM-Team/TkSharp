using System.Runtime.InteropServices;
using BymlLibrary;
using TkSharp.Merging.ChangelogBuilders;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.Mergers.BinaryYaml;

namespace TkSharp.Merging.Mergers.GameData;

public class GameDataMergeTracking(string canonical) : Dictionary<ulong, GameDataMergeTrackingEntry>
{
    private readonly string _canonical = canonical;

    public void Apply()
    {
        BymlTrackingInfo info = new();
        
        foreach (var entry in Values) {
            ApplyEntry(entry, ref info);
        }
    }

    private void ApplyEntry(GameDataMergeTrackingEntry entry, ref BymlTrackingInfo info)
    {
        if (entry.Changes.Count == 0) {
            return;
        }
        
        var baseEntry = entry.BaseEntry;
        var entries = CollectionsMarshal.AsSpan(entry.Changes);
        
        for (var i = 0; i < entry.Changes.Count; i++) {
            BymlChangelogBuilder.LogChangesInline(ref info, ref entries[i], baseEntry);
        }

        BymlMergeTracking tracking = new(_canonical) {
            Type = entry.IsStructTable
                ? "Struct" : null
        };

        foreach (var changelog in entries) {
            BymlMerger.Merge(baseEntry, changelog, tracking);
        }

        tracking.Apply();
    }
}