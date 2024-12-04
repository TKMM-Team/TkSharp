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

    private readonly TkChangelogEntry _fakeEntry = new(
        string.Empty,
        ChangelogEntryType.Changelog,
        TkFileAttributes.None,
        zsDictionaryId: -1);

    public void Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Sarc merged = Sarc.FromBinary(vanillaData);
        var changelogs = new Sarc[inputs.Count];

        for (int i = 0; i < inputs.Count; i++) {
            RentedBuffers<byte>.Entry input = inputs[i];
            changelogs[i] = Sarc.FromBinary(input.Segment);
        }
        
        MergeMany(merged, changelogs);
        merged.Write(output);
    }

    public void Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Sarc merged = Sarc.FromBinary(vanillaData);
        MergeMany(merged, inputs.Select(Sarc.FromBinary));
        merged.Write(output);
    }

    public void MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        Sarc merged = Sarc.FromBinary(@base);
        Sarc changelog = Sarc.FromBinary(input);
        MergeSingle(merged, changelog);
        merged.Write(output);
    }

    private void MergeMany(Sarc merged, IEnumerable<Sarc> changelogs)
    {
        IEnumerable<(string Name, ArraySegment<byte>[] Buffers)> groups = changelogs
            .SelectMany(x => x)
            .GroupBy(x => x.Key, x => x.Value)
            .Select(x => (x.Key, x.ToArray()));
        
        foreach ((string name, ArraySegment<byte>[] buffers) in groups) {
            ArraySegment<byte> last = buffers[^1];
            
            if (!merged.TryGetValue(name, out ArraySegment<byte> vanillaData)) {
                merged[name] = last;
                CalculateRstb(name, last, isFileVanilla: false);
                continue;
            }

            if (_masterMerger.GetMerger(name) is not ITkMerger merger) {
                merged[name] = last;
                CalculateRstb(name, last, isFileVanilla: false);
                continue;
            }

            if (last.Count == 8 && last.AsSpan().Read<ulong>() == DELETED_MARK) {
                merged.Remove(name);
                continue;
            }
            
            // TODO: This is a bit sketchy
            _fakeEntry.Canonical = name;
            
            if (buffers.Length == 1) {
                using (Stream output = merged.OpenWrite(name)) {
                    merger.MergeSingle(_fakeEntry, buffers[0], vanillaData, output);
                }

                goto CalculateMergedRstb;
            }
            
            using (Stream output = merged.OpenWrite(name)) {
                merger.Merge(_fakeEntry, buffers, vanillaData, output);
            }
            
        CalculateMergedRstb:
            CalculateRstb(name, merged[name], isFileVanilla: false);
        }
    }

    private void MergeSingle(in Sarc merged, Sarc changelog)
    {
        foreach ((string name, ArraySegment<byte> data) in changelog) {
            if (!merged.TryGetValue(name, out ArraySegment<byte> vanillaData)) {
                merged[name] = data;
                CalculateRstb(name, data, isFileVanilla: false);
                continue;
            }

            if (_masterMerger.GetMerger(name) is not ITkMerger merger) {
                merged[name] = data;
                CalculateRstb(name, data, isFileVanilla: false);
                continue;
            }

            _fakeEntry.Canonical = name;

            using (Stream output = merged.OpenWrite(name)) {
                merger.MergeSingle(_fakeEntry, data, vanillaData, output);
            }

            CalculateRstb(name, merged[name], isFileVanilla: true);
        }
    }

    private void CalculateRstb(string canonical, Span<byte> data, bool isFileVanilla)
    {
        _resourceSizeCollector.Collect(data.Length, canonical, isFileVanilla, data);
    }
}