using MessageStudio.Formats.BinaryText;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;

namespace TkSharp.Merging.Mergers;

public sealed class MsbtMerger : Singleton<MsbtMerger>, ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        throw new NotSupportedException(
            "Merging memory chained MSBT files is not supported.");
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var baseMsbt = Msbt.FromBinary(vanillaData);

        foreach (var input in inputs) {
            var changelog = Msbt.FromBinary(input);
            foreach ((string key, var value) in changelog) {
                baseMsbt[key] = value;
            }
        }
        
        baseMsbt.WriteBinary(output);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        var baseMsbt = Msbt.FromBinary(@base);
        var changelog = Msbt.FromBinary(input);

        foreach ((string key, var value) in changelog) {
            baseMsbt[key] = value;
        }
        
        baseMsbt.WriteBinary(output);
        
        return MergeResult.Default;
    }
}