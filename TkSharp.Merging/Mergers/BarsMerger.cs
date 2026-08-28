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
        foreach (var (key, asset) in changelog) {
            var fakeMetadata = FakeMetadata.FromBinary(asset.Metadata);

            if (fakeMetadata.Version != FakeMetadata.AMTA_FAKE_VERSION) {
                // Overwrite asset & metadata
                Insert(entry, merged, key, asset, hasFakeMetadata: false);
                goto NotDropped;
            }

            if (fakeMetadata.Flags.HasFlag(FakeMetadataFlags.IsRemoved)) {
                dropped.Add(key);
                continue;
            }

            Insert(entry, merged, key, asset, hasFakeMetadata: true);

        NotDropped:
            // Revert previous drop if applicable
            dropped.Remove(key);
        }
    }

    private static void Insert(TkChangelogEntry entry, AudioResource merged, uint key, AudioResourceAsset changelogAsset, bool hasFakeMetadata)
    {
        if (!merged.TryGetValue(key, out var asset)) {
            // Log an error if the changelog expected this vanilla entry to exist
            if (UseVanilla(changelogAsset) || hasFakeMetadata) {
                TkLog.Instance.LogError(
                    "Expected a vanilla asset in {CanonFile}[{Key:X8}] but none was found. " +
                    "Skipping changelog entry", entry.Canonical, key);
                return;
            }

            merged.Add(key, changelogAsset);
            return;
        }

        // Only apply when the metadata is not a fake metadata entry
        if (!hasFakeMetadata) {
            asset.Metadata = changelogAsset.Metadata;
        }

        // Only apply when the resource is not vanilla
        if (!UseVanilla(changelogAsset)) {
            asset.Asset = changelogAsset.Asset;
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