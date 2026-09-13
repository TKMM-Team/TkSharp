#if EXPERIMENTAL_AINB
using AinbFormat;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers.Ainb;

namespace TkSharp.Merging.Mergers;

public sealed class AinbMerger(IAinbCodec codec, Action<AinbMergeReport>? report = null) : ITkMerger
{
    private readonly AinbBinaryMerger _binary = new(codec);

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        ArraySegment<byte>[] segments = new ArraySegment<byte>[inputs.Count];
        for (int i = 0; i < inputs.Count; i++) segments[i] = inputs[i].Segment;
        return Merge(entry, segments, vanillaData, output);
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var result = _binary.Merge(vanillaData.AsSpan(), inputs.ToArray());
        if (result.Report.Status == AinbMergeStatus.Fallback)
            TkLog.Instance.LogWarning("AINB {Canonical} uses its highest-priority file: {Reason}", entry.Canonical, result.Report.Reason);
        foreach (string warning in result.Report.Warnings)
            TkLog.Instance.LogWarning("AINB {Canonical}: {Warning}", entry.Canonical, warning);
        if (result.Report.Conflicts.Count > 0)
            TkLog.Instance.LogWarning("AINB {Canonical}: {Count} conflicts resolved by priority", entry.Canonical, result.Report.Conflicts.Count);
        report?.Invoke(result.Report);
        output.Write(result.Bytes);
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output) =>
        Merge(entry, new[] { input }, @base, output);
}
#endif
