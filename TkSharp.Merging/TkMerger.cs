using CommunityToolkit.HighPerformance.Buffers;
using LanguageExt;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;
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
    private readonly string? _ipsOutputFolderPath;

    public TkMerger(ITkModWriter output, ITkRom rom, string locale = "USen", string? ipsOutputFolderPath = null)
    {
        _output = output;
        _rom = rom;
        _locale = locale;
        _resourceSizeCollector = new TkResourceSizeCollector(output, rom);
        _sarcMerger = new SarcMerger(this, _resourceSizeCollector);
        _ipsOutputFolderPath = ipsOutputFolderPath;
    }

    public async ValueTask MergeAsync(IEnumerable<TkChangelog> changelogs, CancellationToken ct = default)
    {
        TkChangelog[] tkChangelogs =
            changelogs as TkChangelog[] ?? changelogs.ToArray();

        Task[] tasks = [
            Task.Run(() => MergeIps(tkChangelogs), ct),
            Task.Run(() => MergeSubSdk(_output, tkChangelogs), ct),
            Task.Run(() => MergeCheats(_output, tkChangelogs), ct),
            Task.Run(() => MergeExeFs(_output, tkChangelogs), ct),
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

        MergeSubSdk(_output, tkChangelogs);

        MergeCheats(_output, tkChangelogs);

        MergeExeFs(_output, tkChangelogs);

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
                    MergeCustomTarget(merger, streams[0], streams.Skip(1), changelog, output);
                    break;
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

    public static void MergeCheats(ITkModWriter mergeOutput, IEnumerable<TkChangelog> changelogs)
    {
        IEnumerable<IGrouping<string, TkCheat>> allCheats = changelogs
            .SelectMany(entry => entry.CheatFiles)
            .GroupBy(patch => patch.Name);

        foreach (IGrouping<string, TkCheat> cheats in allCheats) {
            TkCheat merged = new(cheats.Key);
            foreach ((string key, uint[][] bin) in cheats.SelectMany(x => x.Select(cheat => (cheat.Key, cheat.Value)))) {
                merged[key] = bin;
            }

            string outputFile = Path.Combine("cheats", $"{cheats.Key}.txt");

            using Stream output = mergeOutput.OpenWrite(outputFile);
            using StreamWriter writer = new(output);
            merged.WriteText(writer);
        }
    }

    public static void MergeSubSdk(ITkModWriter mergeOutput, IEnumerable<TkChangelog> changelogs)
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
                using Stream output = mergeOutput.OpenWrite($"exefs/subsdk{++index}");
                input.CopyTo(output);
            }
        }

        if (index > 9) {
            TkLog.Instance.LogWarning(
                "{Count} SubSdk files were skipped when merging from the lowest priority mods.",
                index - 9);
        }
    }

    public static void MergeExeFs(ITkModWriter mergeOutput, IEnumerable<TkChangelog> changelogs)
    {
        foreach (TkChangelog changelog in changelogs) {
            if (changelog.Source is null) {
                TkLog.Instance.LogError(
                    "Changelog '{Changelog}' has not been initialized. Try restarting to resolve the issue.",
                    changelog);
                continue;
            }

            foreach (string inputOutput in changelog.ExeFiles.Select(exeFile => $"exefs/{exeFile}")) {
                using Stream input = changelog.Source.OpenRead(inputOutput);
                using Stream output = mergeOutput.OpenWrite(inputOutput);
                input.CopyTo(output);
            }
        }
    }

    private void MergeCustomTarget(ITkMerger merger, Stream @base, IEnumerable<Stream> targets, TkChangelogEntry changelog, Stream output)
    {
        using RentedBuffer<byte> fakeVanilla = _rom.Zstd.Decompress(@base);

        IEnumerable<ArraySegment<byte>> changelogs = TkChangelogBuilder.CreateChangelogsExternal(
            changelog.Canonical, flags: default, fakeVanilla.Segment, targets.Select(stream => {
                using RentedBuffer<byte> alloc = _rom.Zstd.Decompress(stream);
                return alloc.Segment;
            }), changelog.Attributes
        );

        merger.Merge(changelog, changelogs, fakeVanilla.Segment, output);
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

        if (!TkResourceSizeCollector.RequiresDataForCalculation(relativePath)) {
            int size = TkZstd.IsCompressed(input)
                ? TkZstd.GetDecompressedSize(input)
                : (int)input.Length;
            _resourceSizeCollector.Collect(size, relativePath, []);
            input.CopyTo(output);
            return;
        }

        using RentedBuffer<byte> buffer = RentedBuffer<byte>.Allocate(input);
        Span<byte> raw = buffer.Span;

        if (!TkZstd.IsCompressed(raw)) {
            _resourceSizeCollector.Collect(raw.Length, relativePath, raw);
            output.Write(raw);
            return;
        }

        using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(TkZstd.GetDecompressedSize(raw));
        Span<byte> data = decompressed.Span;
        _rom.Zstd.Decompress(raw, data);
        _resourceSizeCollector.Collect(data.Length, relativePath, data);
        output.Write(raw);
    }

    private void CopyMergedToOutput(in MemoryStream input, string relativePath, TkChangelogEntry changelog)
    {
        ArraySegment<byte> buffer = input.GetSpan();
        _resourceSizeCollector.Collect(buffer.Count, relativePath, buffer);

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

        var merged = TkPatch.CreateWithDefaults(_rom.NsoBinaryId, shopParamLimit: 512);

        foreach (TkPatch patch in versionMatchedPatchFiles) {
            foreach ((uint key, uint value) in patch.Entries) {
                merged.Entries[key] = value;
            }
        }

        string ipsFileName = $"{_rom.NsoBinaryId.ToUpper()}.ips";
        string outputFile = _ipsOutputFolderPath is not null
            ? Path.Combine(_ipsOutputFolderPath, ipsFileName)
            : Path.Combine("exefs", ipsFileName);

        using Stream output = _output.OpenWrite(outputFile);
        merged.WriteIps(output);
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
                tuple => (tuple.Entry, tuple.Changelog)
            )
            .Select(GetInputs);
    }

    private MergeTarget GetInputs(IGrouping<TkChangelogEntry, (TkChangelogEntry Entry, TkChangelog Changelog)> group)
    {
        if (GetMerger(group.Key.Canonical) is ITkMerger merger) {
            return (
                Changelog: group.Key,
                Target: (Merger: merger,
                    Streams: group
                        .Select(changelog => changelog.Changelog.Source!.OpenRead(GetRelativeRomFsPath(changelog.Entry)))
                        .ToArray()
                )
            );
        }

        string relativeFilePath = GetRelativeRomFsPath(group.Key);
        return (
            Changelog: group.Key,
            Target: group.Last().Changelog.Source!.OpenRead(relativeFilePath)
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

    private string GetRelativeRomFsPath(TkChangelogEntry entry)
    {
        ReadOnlySpan<char> canon = entry.Canonical;

        if (entry.Versions.Count == 0) {
            return Path.Combine("romfs", entry.Canonical);
        }

        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (canon.Length > 15 && canon[..15] is "Event/EventFlow") {
            ReadOnlySpan<char> eventName = Path.GetFileNameWithoutExtension(canon);
            int targetVersion = _rom.EventFlowVersions.TryGetValue(eventName, out string? version)
                ? GetBestVersion(int.Parse(version), entry.Versions)
                : entry.Versions[0];
            return Path.Combine("romfs", $"{entry.Canonical}{targetVersion}");
        }

        if (canon.Length > 6 && canon[..6] is "Effect") {
            ReadOnlySpan<char> effectName = Path.GetFileNameWithoutExtension(canon);
            int targetVersion = _rom.EffectVersions.TryGetValue(effectName, out string? version)
                ? GetBestVersion(int.Parse(version.AsSpan()[^3..]), entry.Versions)
                : entry.Versions[0];
            return Path.Combine("romfs", $"{entry.Canonical}{targetVersion}");
        }

        return Path.Combine("romfs", $"{entry.Canonical}{GetBestVersion(_rom.GameVersion, entry.Versions)}");
    }

    private static int GetBestVersion(int target, List<int> provided)
    {
        return provided.LastOrDefault(version => target >= version, provided[0]);
    }
}