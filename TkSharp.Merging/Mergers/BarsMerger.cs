using Microsoft.Extensions.Logging;
using Tkmm.AalSharp.Bars;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ChangelogBuilders;
using TkSharp.Merging.Common.AudioResource;

namespace TkSharp.Merging.Mergers;

public sealed class BarsMerger : ITkMerger
{
    public static readonly BarsMerger Instance = new();

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = AudioResource.FromBinary(vanillaData, out var endianness);
        var dropped = new HashSet<uint>();

        foreach (var buffer in inputs) {
            var changelog = entry.Type is ChangelogEntryType.Changelog
                ? AudioResource.FromBinary(buffer.Span)
                : BarsChangelogBuilder.CreateChangelog(TkChangelogBuilderFlags.CustomFiles, buffer.Segment, vanillaData);
            Merge(entry, merged, changelog, ref dropped);
        }

        ApplyDropped(merged, dropped);

        merged.Write(output, endianness);

        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = AudioResource.FromBinary(vanillaData, out var endianness);
        var dropped = new HashSet<uint>();

        foreach (var buffer in inputs) {
            var changelog = entry.Type is ChangelogEntryType.Changelog
                ? AudioResource.FromBinary(buffer)
                : BarsChangelogBuilder.CreateChangelog(TkChangelogBuilderFlags.CustomFiles, buffer, vanillaData);
            Merge(entry, merged, changelog, ref dropped);
        }

        ApplyDropped(merged, dropped);

        merged.Write(output, endianness);

        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        var merged = AudioResource.FromBinary(@base, out var endianness);
        var dropped = new HashSet<uint>();
        var changelog = entry.Type is ChangelogEntryType.Changelog
            ? AudioResource.FromBinary(input)
            : BarsChangelogBuilder.CreateChangelog(TkChangelogBuilderFlags.CustomFiles, input, @base);

        Merge(entry, merged, changelog, ref dropped);
        ApplyDropped(merged, dropped);

        merged.Write(output, endianness);

        return MergeResult.Default;
    }

    private static void Merge(TkChangelogEntry entry, AudioResource merged, AudioResource changelog, ref HashSet<uint> dropped)
    {
        foreach (var (key, changelogAsset) in changelog) {
            if (FakeMetadata.FromBinary(changelogAsset.Metadata) is {
                    Version: FakeMetadata.AMTA_FAKE_VERSION, Flags: FakeMetadataFlags.IsRemoved
                }) {
                dropped.Add(key);
                continue;
            }

            if (!merged.TryGetValue(key, out var asset)) {
                // Log an error if the changelog expected this vanilla entry to exist
                if (UseVanilla(changelogAsset)) {
                    TkLog.Instance.LogError(
                        "Expected a vanilla asset in {CanonFile}[{Key:X8}] but none was found. " +
                        "Skipping changelog entry", entry.Canonical, key);
                    continue;
                }

                merged.Add(key, changelogAsset);
                continue;
            }
            
            asset.Metadata = changelogAsset.Metadata;
            asset.IsPublic = changelogAsset.IsPublic;
            
            if (!UseVanilla(changelogAsset)) {
                asset.Asset = changelogAsset.Asset;
            }
            
            dropped.Remove(key);
        }
    }

    private static bool UseVanilla(AudioResourceAsset asset)
    {
        return asset.Asset.SequenceEqual(BarsChangelogBuilder.IsVanillaMark);
    }

    private static void ApplyDropped(AudioResource merged, HashSet<uint> dropped)
    {
        foreach (uint key in dropped) {
            merged.Remove(key);
        }
    }
}
