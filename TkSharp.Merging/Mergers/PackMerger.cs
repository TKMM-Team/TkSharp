using Revrs.Extensions;
using SarcLibrary;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.PackFile;

namespace TkSharp.Merging.Mergers;

public sealed class PackMerger(TkPackFileCollector packFileCollector) : ITkMerger
{
    private const ulong DELETED_MARK = 0x44564D5243534B54;
    
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        // Copy the vanillaData because it will be
        // disposed later and the Sarc will persist
        var merged = Sarc.FromBinary(vanillaData.ToArray());
        var changelogs = new Sarc[inputs.Count];

        for (int i = 0; i < inputs.Count; i++) {
            var input = inputs[i];
            changelogs[i] = Sarc.FromBinary(input.Segment);
        }
        
        MergeMany(merged, changelogs);
        packFileCollector.RegisterPackFile(entry.Canonical, merged);
        
        return MergeResult.DelayWrite;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        // Copy the vanillaData because it will be
        // disposed later and the Sarc will persist
        var merged = Sarc.FromBinary(vanillaData.ToArray());
        MergeMany(merged, inputs.Select(Sarc.FromBinary));
        packFileCollector.RegisterPackFile(entry.Canonical, merged);
        
        return MergeResult.DelayWrite;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        // Copy the vanillaData because it will be
        // disposed later and the Sarc will persist
        var merged = Sarc.FromBinary(@base.ToArray());
        var changelog = Sarc.FromBinary(input);
        MergeSingle(merged, changelog);
        packFileCollector.RegisterPackFile(entry.Canonical, merged);
        
        return MergeResult.DelayWrite;
    }

    private static void MergeMany(Sarc merged, IEnumerable<Sarc> changelogs)
    {
        IEnumerable<(string Name, ArraySegment<byte>[] Buffers)> groups = changelogs
            .SelectMany(x => x)
            .GroupBy(x => x.Key, x => x.Value)
            .Select(x => (x.Key, x.ToArray()));
        
        foreach ((string name, var buffers) in groups) {
            var data = buffers[^1];
            if (IsRemovedEntry(data)) {
                merged.Remove(name);
            }
        }
    }

    private static void MergeSingle(in Sarc merged, Sarc changelog)
    {
        foreach ((string name, var data) in changelog) {
            if (IsRemovedEntry(data)) {
                merged.Remove(name);
            }
        }
    }

    public static bool IsRemovedEntry(ReadOnlySpan<byte> data)
    {
        return data.Length == 8 && data.Read<ulong>() == DELETED_MARK;
    }
}