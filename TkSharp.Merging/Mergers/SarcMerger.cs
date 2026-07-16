using Revrs;
using Revrs.Extensions;
using SarcLibrary;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ResourceSizeTable;

namespace TkSharp.Merging.Mergers;

public sealed class SarcMerger(TkMerger masterMerger, TkResourceSizeCollector resourceSizeCollector) : ITkMerger
{
    private const ulong DELETED_MARK = 0x44564D5243534B54;
    private readonly TkMerger _masterMerger = masterMerger;
    private readonly TkResourceSizeCollector _resourceSizeCollector = resourceSizeCollector;

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Sarc.FromBinary(vanillaData);
        var changelogs = new Sarc[inputs.Count];

        for (var i = 0; i < inputs.Count; i++) {
            var input = inputs[i];
            changelogs[i] = Sarc.FromBinary(input.Segment);
        }
        
        MergeMany(entry.Canonical, merged, changelogs);
        merged.Write(output);
        
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Sarc.FromBinary(vanillaData);
        MergeMany(entry.Canonical, merged, inputs.Select(Sarc.FromBinary));
        merged.Write(output);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        var merged = Sarc.FromBinary(@base);
        var changelog = Sarc.FromBinary(input);
        MergeSingle(entry.Canonical, merged, changelog);
        merged.Write(output);
        
        return MergeResult.Default;
    }

    private void MergeMany(string parentCanonical, Sarc merged, IEnumerable<Sarc> changelogs)
    {
        IEnumerable<(string Name, ArraySegment<byte>[] Buffers)> groups = changelogs
            .SelectMany(x => x)
            .GroupBy(x => x.Key, x => x.Value)
            .Select(x => (x.Key, x.ToArray()));
        
        TkChangelogEntry fakeEntry = new(
            string.Empty,
            ChangelogEntryType.Changelog,
            TkFileAttributes.None,
            zsDictionaryId: -1);
        
        foreach (var (name, buffers) in groups) {
            // Legacy SARC changelogs may contain deletion marks from older TKMM versions.
            // Ignore them so files added by game updates are not removed.
            var effective = buffers.Where(buffer => !IsRemovedEntry(buffer)).ToArray();
            if (effective.Length == 0) {
                continue;
            }

            var last = effective[^1];
            
            if (!merged.TryGetValue(name, out var vanillaData)) {
                merged[name] = last;
                continue;
            }

            if (_masterMerger.GetMerger(name) is not { } merger) {
                merged[name] = last;
                continue;
            }
            
            fakeEntry.Canonical = $"{parentCanonical}/{name}";
            
            if (effective.Length == 1) {
                using Stream output = merged.OpenWrite(name);
                merger.MergeSingle(fakeEntry, effective[0], vanillaData, output);
                continue;
            }

            using (Stream output = merged.OpenWrite(name)) {
                merger.Merge(fakeEntry, effective, vanillaData, output);
            }
        }
    }

    private void MergeSingle(string parentCanonical, in Sarc merged, Sarc changelog)
    {
        TkChangelogEntry fakeEntry = new(
            string.Empty,
            ChangelogEntryType.Changelog,
            TkFileAttributes.None,
            zsDictionaryId: -1);
        
        foreach (var (name, data) in changelog) {
            // Legacy SARC changelogs may contain deletion marks from older TKMM versions.
            // Ignore them so files added by game updates are not removed.
            if (IsRemovedEntry(data)) {
                continue;
            }

            if (!merged.TryGetValue(name, out var vanillaData)) {
                merged[name] = data;
                continue;
            }

            if (_masterMerger.GetMerger(name) is not { } merger) {
                merged[name] = data;
                continue;
            }

            fakeEntry.Canonical = $"{parentCanonical}/{name}";

            using Stream output = merged.OpenWrite(name);
            merger.MergeSingle(fakeEntry, data, vanillaData, output);
        }
    }

    private static bool IsRemovedEntry(ReadOnlySpan<byte> data)
    {
        return data.Length == 8 && data.Read<ulong>() == DELETED_MARK;
    }
}