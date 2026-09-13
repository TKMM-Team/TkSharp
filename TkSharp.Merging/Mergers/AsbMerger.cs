using AsbLibrary;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers.Asb;

namespace TkSharp.Merging.Mergers;

public sealed class AsbMerger : Singleton<AsbMerger>, ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs,
        ArraySegment<byte> vanillaData, Stream output)
    {
        var documents = new List<AsbDocument>();
        foreach (var input in inputs)
        {
            documents.Add(AsbCodec.Read(input.Span.ToArray()));
        }

        return Merge(entry, AsbCodec.Read(vanillaData.ToArray()), documents, output);
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs,
        ArraySegment<byte> vanillaData, Stream output)
    {
        var documents = inputs.Select(input => AsbCodec.Read(input.ToArray())).ToArray();
        return Merge(entry, AsbCodec.Read(vanillaData.ToArray()), documents, output);
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input,
        ArraySegment<byte> @base, Stream output)
    {
        return Merge(entry, AsbCodec.Read(@base.ToArray()), [AsbCodec.Read(input.ToArray())], output);
    }

    private static MergeResult Merge(TkChangelogEntry entry, AsbDocument vanilla,
        IReadOnlyList<AsbDocument> inputs, Stream output)
    {
        var (document, report) = AsbMergeEngine.Merge(vanilla, inputs);
        output.Write(AsbCodec.Write(document));
        LogReport(entry.Canonical, report);
        return MergeResult.Default;
    }

    private static void LogReport(string canonical, AsbMergeReport report)
    {
        if (report.NodeConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "ASB merge for {Canonical} resolved {Count} node conflicts by mod priority: {Conflicts}",
                canonical, report.NodeConflicts.Count, string.Join(", ", report.NodeConflicts));
        }

        if (report.CommandConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "ASB merge for {Canonical} resolved {Count} command conflicts by mod priority: {Conflicts}",
                canonical, report.CommandConflicts.Count, string.Join(", ", report.CommandConflicts));
        }

        if (report.SectionConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "ASB merge for {Canonical} resolved whole-section conflicts by mod priority: {Conflicts}",
                canonical, string.Join(", ", report.SectionConflicts));
        }

        if (report.GuidConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "ASB merge for {Canonical} contains duplicate node GUIDs: {Conflicts}",
                canonical, string.Join(", ", report.GuidConflicts));
        }
    }
}

public sealed class BaevMerger : Singleton<BaevMerger>, ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs,
        ArraySegment<byte> vanillaData, Stream output)
    {
        var documents = new List<BaevDocument>();
        foreach (var input in inputs)
        {
            documents.Add(BaevCodec.Read(input.Span.ToArray()));
        }

        return Merge(entry, BaevCodec.Read(vanillaData.ToArray()), documents, output);
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs,
        ArraySegment<byte> vanillaData, Stream output)
    {
        var documents = inputs.Select(input => BaevCodec.Read(input.ToArray())).ToArray();
        return Merge(entry, BaevCodec.Read(vanillaData.ToArray()), documents, output);
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input,
        ArraySegment<byte> @base, Stream output)
    {
        return Merge(entry, BaevCodec.Read(@base.ToArray()), [BaevCodec.Read(input.ToArray())], output);
    }

    private static MergeResult Merge(TkChangelogEntry entry, BaevDocument vanilla,
        IReadOnlyList<BaevDocument> inputs, Stream output)
    {
        var (document, report) = AsbMergeEngine.Merge(vanilla, inputs);
        output.Write(BaevCodec.Write(document));
        LogReport(entry.Canonical, report);
        return MergeResult.Default;
    }

    private static void LogReport(string canonical, BaevMergeReport report)
    {
        if (report.NodeConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "BAEV merge for {Canonical} resolved {Count} node conflicts by mod priority: {Conflicts}",
                canonical, report.NodeConflicts.Count, string.Join(", ", report.NodeConflicts));
        }

        if (report.EventConflicts.Count > 0)
        {
            TkLog.Instance.LogWarning(
                "BAEV merge for {Canonical} resolved {Count} event conflicts by mod priority: {Conflicts}",
                canonical, report.EventConflicts.Count, string.Join(", ", report.EventConflicts));
        }
    }
}
