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

    public void Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Sarc merged = Sarc.FromBinary(vanillaData);
        var changelogs = new Sarc[inputs.Count];

        for (int i = 0; i < inputs.Count; i++) {
            RentedBuffers<byte>.Entry input = inputs[i];
            changelogs[i] = Sarc.FromBinary(input.Segment);
        }
        
        MergeMany(entry.Canonical, merged, changelogs);
        merged.Write(output);
    }

    public void Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Sarc merged = Sarc.FromBinary(vanillaData);
        MergeMany(entry.Canonical, merged, inputs.Select(Sarc.FromBinary));
        merged.Write(output);
    }

    public void MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        Sarc merged = Sarc.FromBinary(@base);
        Sarc changelog = Sarc.FromBinary(input);
        MergeSingle(entry.Canonical, merged, changelog);
        merged.Write(output);
    }

    public void CalculateRstb(Span<byte> input)
    {
        RevrsReader reader = new(input);
        ImmutableSarc sarc = new(ref reader);

        foreach ((string name, Span<byte> data) in sarc) {
            _resourceSizeCollector.Collect(data.Length, name, data);
        }
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
        
        foreach ((string name, ArraySegment<byte>[] buffers) in groups) {
            ArraySegment<byte> last = buffers[^1];
            
            if (!merged.TryGetValue(name, out ArraySegment<byte> vanillaData)) {
                merged[name] = last;
                CalculateRstb(parentCanonical, name, last);
                continue;
            }

            if (IsRemovedEntry(last)) {
                merged.Remove(name);
                continue;
            }

            if (_masterMerger.GetMerger(name) is not ITkMerger merger) {
                merged[name] = last;
                CalculateRstb(parentCanonical, name, last);
                continue;
            }
            
            fakeEntry.Canonical = name;
            
            switch (buffers.Length) {
                case 1: {
                    using Stream output = merged.OpenWrite(name);
                    merger.MergeSingle(fakeEntry, buffers[0], vanillaData, output);
                    goto CalculateMergedRstb;
                }
                case 2 when IsRemovedEntry(buffers[0]): {
                    Stream output = merged.OpenWrite(name);
                    merger.MergeSingle(fakeEntry, last, vanillaData, output);
                    goto CalculateMergedRstb;
                }
            }

            using (Stream output = merged.OpenWrite(name)) {
                merger.Merge(fakeEntry, buffers.Where(buffer => !IsRemovedEntry(buffer)), vanillaData, output);
            }
            
        CalculateMergedRstb:
            CalculateRstb(parentCanonical, name, merged[name]);
        }
    }

    private void MergeSingle(string parentCanonical, in Sarc merged, Sarc changelog)
    {
        TkChangelogEntry fakeEntry = new(
            string.Empty,
            ChangelogEntryType.Changelog,
            TkFileAttributes.None,
            zsDictionaryId: -1);
        
        foreach ((string name, ArraySegment<byte> data) in changelog) {
            if (!merged.TryGetValue(name, out ArraySegment<byte> vanillaData)) {
                merged[name] = data;
                CalculateRstb(parentCanonical, name, data);
                continue;
            }
            
            if (IsRemovedEntry(data)) {
                merged.Remove(name);
                continue;
            }

            if (_masterMerger.GetMerger(name) is not ITkMerger merger) {
                merged[name] = data;
                CalculateRstb(parentCanonical, name, data);
                continue;
            }

            fakeEntry.Canonical = name;

            using (Stream output = merged.OpenWrite(name)) {
                merger.MergeSingle(fakeEntry, data, vanillaData, output);
            }

            CalculateRstb(parentCanonical, name, merged[name]);
        }
    }

    private void CalculateRstb(ReadOnlySpan<char> parentCanonical, string name, Span<byte> data)
    {
        if (Path.GetExtension(parentCanonical) is not ".pack") {
            return;
        }
        
        _resourceSizeCollector.Collect(data.Length, name, data);
    }

    private static bool IsRemovedEntry(ReadOnlySpan<byte> data)
    {
        return data.Length == 8 && data.Read<ulong>() == DELETED_MARK;
    }
}