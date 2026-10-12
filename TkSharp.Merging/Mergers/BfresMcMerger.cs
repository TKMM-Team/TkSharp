using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Bfres;

namespace TkSharp.Merging.Mergers;

public sealed class BfresMcMerger(ITkRom rom) : ITkMerger
{
    private readonly BfresMcProcessor _processor = new(rom);

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
        => WriteOutput(output, Process(entry, input));

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
        => WriteOutput(output, Process(entry, inputs[^1].Segment));

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
        => WriteOutput(output, Process(entry, inputs.Last()));

    private byte[] Process(TkChangelogEntry entry, ArraySegment<byte> mcData)
    {
        if (_processor.ProcessIfChanged(mcData) is not { } processed) {
            return mcData.ToArray();
        }

        entry.RuntimeResourceSizeOverride = 0;
        return processed;
    }

    private static MergeResult WriteOutput(Stream output, byte[] data)
    {
        output.Write(data);
        return MergeResult.Default;
    }
}
