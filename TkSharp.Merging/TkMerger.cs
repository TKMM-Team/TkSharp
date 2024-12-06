using CommunityToolkit.HighPerformance.Buffers;
using LanguageExt;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers;
using TkSharp.Merging.ResourceSizeTable;
using MergeTarget = (TkSharp.Core.Models.TkChangelogEntry Changelog, LanguageExt.Either<(TkSharp.Merging.ITkMerger Merger, System.IO.Stream[] Streams), System.IO.Stream> Target);

namespace TkSharp.Merging;

public sealed class TkMerger
{
    private readonly ITkModWriter _output;
    private readonly ITkRom _rom;
    private readonly string _locale;
    private readonly TkResourceSizeCollector _resourceSizeCollector;
    private readonly SarcMerger _sarcMerger;

    public TkMerger(ITkModWriter output, ITkRom rom, string locale = "USen")
    {
        _output = output;
        _rom = rom;
        _locale = locale;
        _resourceSizeCollector = new TkResourceSizeCollector(output, rom);
        _sarcMerger = new SarcMerger(this, _resourceSizeCollector);
    }

    public async ValueTask MergeAsync(IEnumerable<TkChangelog> changelogs, CancellationToken ct = default)
    {
        TkChangelog[] tkChangelogs =
            changelogs as TkChangelog[] ?? changelogs.ToArray();

        Task[] tasks = [
            Task.Run(() => MergeIps(tkChangelogs), ct),
            Task.Run(() => MergeSubSdk(tkChangelogs), ct),
            Task.Run(() => CopyCheats(tkChangelogs), ct),
            Task.Run(() => MergeMals(tkChangelogs), ct),
            .. GetTargets(tkChangelogs)
                .Select(entry => Task.Run(() => MergeTarget(entry.Changelog, entry.Target), ct))
        ];

        await Task.WhenAll(tasks);
        
        _resourceSizeCollector.Write();
    }

    public void Merge(IEnumerable<TkChangelog> changelogs)
    {
        TkChangelog[] tkChangelogs =
            changelogs as TkChangelog[] ?? changelogs.ToArray();

        MergeIps(tkChangelogs);

        MergeSubSdk(tkChangelogs);

        CopyCheats(tkChangelogs);

        MergeMals(tkChangelogs);

        foreach ((TkChangelogEntry changelog, Either<(ITkMerger, Stream[]), Stream> target) in GetTargets(tkChangelogs)) {
            MergeTarget(changelog, target);
        }
        
        _resourceSizeCollector.Write();
    }

    public void MergeTarget(TkChangelogEntry changelog, Either<(ITkMerger, Stream[]), Stream> target)
    {
        string relativeFilePath = _rom.CanonicalToRelativePath(changelog.Canonical, changelog.Attributes);
        using MemoryStream output = new();

        switch (target.Case) {
            case (ITkMerger merger, Stream[] { Length: > 1 } streams): {
                using RentedBuffer<byte> vanilla = _rom.GetVanilla(relativeFilePath);
                if (vanilla.IsEmpty) {
                    CopyToOutput(streams[^1], relativeFilePath, changelog);
                    return;
                }

                using RentedBuffers<byte> inputs = RentedBuffers<byte>.Allocate(streams);
                merger.Merge(changelog, inputs, vanilla.Segment, output);
                break;
            }
            case (ITkMerger merger, Stream[] { Length: 1 } streams): {
                using RentedBuffer<byte> vanilla = _rom.GetVanilla(relativeFilePath);
                using Stream single = streams[0];
                if (vanilla.IsEmpty) {
                    CopyToOutput(single, relativeFilePath, changelog);
                    return;
                }

                using RentedBuffer<byte> input = RentedBuffer<byte>.Allocate(single);
                merger.MergeSingle(changelog, input.Segment, vanilla.Segment, output);
                break;
            }
            case Stream copy:
                CopyToOutput(copy, relativeFilePath, changelog);
                return;
        }

        CopyMergedToOutput(output, relativeFilePath, changelog);
    }

    private void CopyToOutput(in Stream input, string relativePath, TkChangelogEntry changelog)
    {
        ReadOnlySpan<char> extension = Path.GetExtension(changelog.Canonical.AsSpan());

        if (extension is ".pack") {
            // This buffer may be mutated, so never
            // write is directly to the output 
            using RentedBuffer<byte> src = _rom.Zstd.Decompress(input);
            _sarcMerger.CalculateRstb(src.Span);
            input.Seek(0, SeekOrigin.Begin);
        }

        using Stream output = _output.OpenWrite(Path.Combine("romfs", relativePath));

        if (!TkResourceSizeCollector.RequiresDataForCalculation(extension)) {
            int size = TkZstd.IsCompressed(input)
                ? TkZstd.GetDecompressedSize(input)
                : (int)input.Length;
            _resourceSizeCollector.Collect(size, relativePath, changelog.Canonical, []);
            input.CopyTo(output);
            return;
        }

        using RentedBuffer<byte> buffer = RentedBuffer<byte>.Allocate(input);
        Span<byte> raw = buffer.Span;

        if (!TkZstd.IsCompressed(raw)) {
            _resourceSizeCollector.Collect(raw.Length, relativePath, changelog.Canonical, raw);
            output.Write(raw);
            return;
        }
        
        using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(TkZstd.GetDecompressedSize(raw));
        Span<byte> data = decompressed.Span;
        _rom.Zstd.Decompress(raw, data);
        _resourceSizeCollector.Collect(data.Length, relativePath, changelog.Canonical, data);
        output.Write(raw);
    }

    private void CopyMergedToOutput(in MemoryStream input, string relativePath, TkChangelogEntry changelog)
    {
        if (!input.TryGetBuffer(out ArraySegment<byte> buffer)) {
            buffer = input.ToArray();
        }

        _resourceSizeCollector.Collect(buffer.Count,
            relativePath, changelog.Canonical, buffer);

        using Stream output = _output.OpenWrite(
            Path.Combine("romfs", relativePath));

        if (changelog.Attributes.HasFlag(TkFileAttributes.HasZsExtension)) {
            using SpanOwner<byte> compressed = SpanOwner<byte>.Allocate(buffer.Count);
            Span<byte> result = compressed.Span;
            int compressedSize = _rom.Zstd.Compress(buffer, result, changelog.ZsDictionaryId);
            output.Write(result[..compressedSize]);
            return;
        }

        output.Write(buffer);
    }

    private void MergeIps(TkChangelog[] changelogs)
    {
        IEnumerable<TkPatch> versionMatchedPatchFiles = changelogs
            .SelectMany(entry => entry.PatchFiles
                .Where(patch => patch.NsoBinaryId.Equals(_rom.NsoBinaryId, StringComparison.InvariantCultureIgnoreCase)));

        TkPatch merged = new(_rom.NsoBinaryId);
        foreach (TkPatch patch in versionMatchedPatchFiles) {
            foreach ((uint key, uint value) in patch.Entries) {
                merged.Entries[key] = value;
            }
        }

        using Stream output = _output.OpenWrite($"exefs/{_rom.NsoBinaryId.ToUpper()}.ips");
        merged.WriteIps(output);
    }

    private void MergeSubSdk(TkChangelog[] changelogs)
    {
        int index = 0;

        foreach (TkChangelog changelog in changelogs.Reverse()) {
            if (changelog.Source is null) {
                TkLog.Instance.LogError(
                    "Changelog '{Changelog}' has not been initialized. Try restarting to resolve the issue.",
                    changelog);
                continue;
            }

            foreach (string subSdkFile in changelog.SubSdkFiles) {
                if (index > 9) {
                    index++;
                    continue;
                }

                using Stream input = changelog.Source.OpenRead($"exefs/{subSdkFile}");
                using Stream output = _output.OpenWrite($"exefs/subsdk{++index}");
                input.CopyTo(output);
            }
        }

        if (index > 9) {
            TkLog.Instance.LogWarning(
                "{Count} SubSdk files were skipped when merging from the lowest priority mods.",
                index - 9);
        }
    }

    private void CopyCheats(TkChangelog[] changelogs)
    {
        foreach (TkChangelog changelog in changelogs) {
            if (changelog.Source is null) {
                TkLog.Instance.LogError(
                    "Changelog '{Changelog}' has not been initialized. Try restarting to resolve the issue.",
                    changelog);
                continue;
            }

            foreach (string inputOutput in changelog.CheatFiles.Select(cheatFile => $"cheats/{cheatFile}")) {
                using Stream input = changelog.Source.OpenRead(inputOutput);
                using Stream output = _output.OpenWrite(inputOutput);
                input.CopyTo(output);
            }
        }
    }

    private void MergeMals(TkChangelog[] changelogs)
    {
        using RentedBuffers<byte> combinedBuffers = RentedBuffers<byte>.Allocate(
            changelogs
                .SelectMals(_locale)
                .Select(entry => entry.Changelog.Source!.OpenRead($"romfs/{entry.MalsFile}"))
                .ToArray());

        if (combinedBuffers.Count == 0) {
            return;
        }

        string canonical = $"Mals/{_locale}.Product.sarc";
        const TkFileAttributes attributes = TkFileAttributes.HasZsExtension | TkFileAttributes.IsProductFile;
        TkChangelogEntry fakeEntry = new(canonical, ChangelogEntryType.Changelog, attributes, zsDictionaryId: 1);
        string relativeFilePath = _rom.CanonicalToRelativePath(canonical, attributes);

        using RentedBuffer<byte> vanilla = _rom.GetVanilla(relativeFilePath);
        using MemoryStream ms = new();
        _sarcMerger.Merge(fakeEntry, combinedBuffers, vanilla.Segment, ms);

        CopyMergedToOutput(ms, relativeFilePath, fakeEntry);
    }

    private IEnumerable<MergeTarget> GetTargets(TkChangelog[] changelogs)
    {
        return changelogs
            .SelectMany(
                changelog => changelog.ChangelogFiles
                    .Select(entry => (Entry: entry, Changelog: changelog))
            )
            .GroupBy(
                tuple => tuple.Entry,
                tuple => tuple.Changelog
            )
            .Select(GetInputs);
    }

    private MergeTarget GetInputs(IGrouping<TkChangelogEntry, TkChangelog> group)
    {
        string relativeFilePath = Path.Combine("romfs", group.Key.Canonical);

        if (GetMerger(group.Key.Canonical) is ITkMerger merger) {
            return (
                Changelog: group.Key,
                Target: (Merger: merger,
                    Streams: group
                        .Select(changelog => changelog.Source!.OpenRead(relativeFilePath))
                        .ToArray()
                )
            );
        }

        return (
            Changelog: group.Key,
            Target: group.Last().Source!.OpenRead(relativeFilePath)
        );
    }

    public ITkMerger? GetMerger(ReadOnlySpan<char> canonical)
    {
        return canonical switch {
            "GameData/GameDataList.Product.byml" => GameDataMerger.Instance,
            "RSDB/Tag.Product.rstbl.byml" => RsdbTagMerger.Instance,
            "RSDB/GameSafetySetting.Product.rstbl.byml" => RsdbRowMergers.NameHash,
            "RSDB/RumbleCall.Product.rstbl.byml" or "RSDB/UIScreen.Product.rstbl.byml" => RsdbRowMergers.Name,
            "RSDB/TagDef.Product.rstbl.byml" => RsdbRowMergers.FullTagId,
            "RSDB/ActorInfo.Product.rstbl.byml" or
                "RSDB/AttachmentActorInfo.Product.rstbl.byml" or
                "RSDB/Challenge.Product.rstbl.byml" or
                "RSDB/EnhancementMaterialInfo.Product.rstbl.byml" or
                "RSDB/EventPlayEnvSetting.Product.rstbl.byml" or
                "RSDB/EventSetting.Product.rstbl.byml" or
                "RSDB/GameActorInfo.Product.rstbl.byml" or
                "RSDB/GameAnalyzedEventInfo.Product.rstbl.byml" or
                "RSDB/GameEventBaseSetting.Product.rstbl.byml" or
                "RSDB/GameEventMetadata.Product.rstbl.byml" or
                "RSDB/LoadingTips.Product.rstbl.byml" or
                "RSDB/Location.Product.rstbl.byml" or
                "RSDB/LocatorData.Product.rstbl.byml" or
                "RSDB/PouchActorInfo.Product.rstbl.byml" or
                "RSDB/XLinkPropertyTable.Product.rstbl.byml" or
                "RSDB/XLinkPropertyTableList.Product.rstbl.byml" => RsdbRowMergers.RowId,
            _ => Path.GetExtension(canonical) switch {
                ".bfarc" or ".bkres" or ".blarc" or ".genvb" or ".pack" or ".ta" => _sarcMerger,
                ".byml" or ".bgyml" => BymlMerger.Instance,
                ".msbt" => MsbtMerger.Instance,
                _ => null
            }
        };
    }
}