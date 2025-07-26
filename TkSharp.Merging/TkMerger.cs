using CommunityToolkit.HighPerformance.Buffers;
using LanguageExt;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers;
using TkSharp.Merging.PackFile;
using TkSharp.Merging.ResourceSizeTable;
using MergeTarget = (TkSharp.Core.Models.TkChangelogEntry Changelog, LanguageExt.Either<(TkSharp.Merging.ITkMerger Merger, System.IO.Stream[] Streams), System.IO.Stream> Target);

namespace TkSharp.Merging;

public enum MergeResult
{
    Default,
    DelayWrite
}

public sealed class TkMerger
{
    private readonly ITkModWriter _output;
    private readonly ITkRom _rom;
    private readonly string _locale;
    private readonly TkResourceSizeCollector _resourceSizeCollector;
    private readonly SarcMerger _sarcMerger;
    private readonly PackMerger _packMerger;
    private readonly string? _ipsOutputFolderPath;
    private readonly TkPackFileCollector _packFileCollector;

    public TkMerger(ITkModWriter output, ITkRom rom, string locale = "USen", string? ipsOutputFolderPath = null)
    {
        _output = output;
        _rom = rom;
        _locale = locale;
        _resourceSizeCollector = new TkResourceSizeCollector(output, rom);
        _sarcMerger = new SarcMerger(this, _resourceSizeCollector);
        _ipsOutputFolderPath = ipsOutputFolderPath;
        _packFileCollector = new TkPackFileCollector(this, _resourceSizeCollector, _rom);
        _packMerger = new PackMerger(_packFileCollector);
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

        await Task.Run(() => {
            _packFileCollector.Write();
            _resourceSizeCollector.Write();
        }, ct);
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

        _packFileCollector.Write();
        _resourceSizeCollector.Write();
    }

    public void MergeTarget(TkChangelogEntry changelog, Either<(ITkMerger, Stream[]), Stream> target)
    {
        string relativeFilePath = changelog.RuntimeArchiveCanonicals.Count > 0
            ? _rom.CanonicalToRelativePath(changelog.Canonical, TkFileAttributes.None)
            : _rom.CanonicalToRelativePath(changelog.Canonical, changelog.Attributes);

        if (target.Case is (ITkMerger _, Stream[] { Length: 0 }) or null) {
            if (_rom.GetVanilla(relativeFilePath, out bool isFoundMissing) is { IsEmpty: false } vanilla) {
                CopyVanillaPlaceholderToOutput(vanilla, changelog);
                return;
            }

            TkLog.Instance.LogWarning(
                "Failed to copy placeholder file {FileName} because the vanilla file could not be found. {IsFoundMissing}",
                relativeFilePath, isFoundMissing
            );
            return;
        }

        using MemoryStream output = new();
        var result = MergeResult.Default;

        switch (target.Case) {
            case (ITkMerger merger, Stream[] { Length: > 1 } streams): {
                // TODO: It would be more efficient to avoid
                // GetVanilla on nested files. Checking loaded
                // pack files first would be optimal. 
                using RentedBuffer<byte> vanilla = _rom.GetVanilla(relativeFilePath, out bool isFoundMissing);
                
                if (isFoundMissing) {
                    TkLog.Instance.LogWarning(
                        "The changelog for '{Canonical}' could not be merged because the vanilla file could not be found",
                        changelog.Canonical);
                    return;
                }
                
                if (vanilla.IsEmpty) {
                    MergeCustomTarget(merger, streams[0], streams.AsSpan(1..), changelog, output);
                    break;
                }

                using RentedBuffers<byte> inputs = RentedBuffers<byte>.Allocate(streams, disposeStreams: true);
                result = merger.Merge(changelog, inputs, vanilla.Segment, output);
                break;

            }
            case (ITkMerger merger, Stream[] { Length: 1 } streams): {
                using RentedBuffer<byte> vanilla = _rom.GetVanilla(relativeFilePath, out bool isFoundMissing);
                Stream single = streams[0];
                
                if (isFoundMissing) {
                    TkLog.Instance.LogWarning(
                        "The changelog for '{Canonical}' could not be merged because the vanilla file could not be found",
                        changelog.Canonical);
                    return;
                }
                
                if (vanilla.IsEmpty) {
                    CopyToOutput(single, relativeFilePath, changelog);
                    return;
                }
                
                using RentedBuffer<byte> input = RentedBuffer<byte>.Allocate(single);
                result = merger.MergeSingle(changelog, input.Segment, vanilla.Segment, output);
                single.Dispose();
                break;
            }
            case Stream copy:
                CopyToOutput(copy, relativeFilePath, changelog);
                return;       
        }

        if (result is MergeResult.DelayWrite) {
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

            IEnumerable<(string, byte[])> subSkdFileContents = changelog.SubSdkFiles.Select(file => {
                using Stream input = changelog.Source.OpenRead($"exefs/{file}");
                byte[] buffer = new byte[input.Length];
                input.ReadExactly(buffer, 0, buffer.Length);
                return (file, buffer);
            }).DistinctBy(x => x);

            foreach ((string _, byte[] data) in subSkdFileContents) {
                if (index > 9) {
                    index++;
                    continue;
                }

                using Stream output = mergeOutput.OpenWrite($"exefs/subsdk{++index}");
                output.Write(data);
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

    internal void MergeCustomTarget(ITkMerger merger, Stream @base, ReadOnlySpan<Stream> targets, TkChangelogEntry changelog, Stream output)
    {
        using RentedBuffer<byte> fakeVanilla = _rom.Zstd.Decompress(@base);
        MergeCustomTarget(merger, fakeVanilla.Segment, targets, changelog, output);
    }

    internal static void MergeCustomTarget(ITkMerger merger, ArraySegment<byte> @base, ReadOnlySpan<Stream> targets, TkChangelogEntry changelog, Stream output)
    {
        using RentedBuffers<byte> targetsBuffer = RentedBuffers<byte>.Allocate(targets);
        ArraySegment<ArraySegment<byte>> changelogs = TkChangelogBuilder.CreateChangelogsExternal(
            changelog.Canonical, flags: default, @base, targetsBuffer, changelog.Attributes
        );

        if (changelogs.Count == 0) {
            output.Write(@base);
            return;
        }

        merger.Merge(changelog, changelogs, @base, output);
    }

    private void CopyToOutput(Stream input, string relativePath, TkChangelogEntry changelog)
    {
        if (changelog.RuntimeArchiveCanonicals.Count != 0) {
            _packFileCollector.Collect(changelog, input);
            return;
        }

        using Stream output = _output.OpenWrite(Path.Combine("romfs", relativePath));

        if (!TkResourceSizeCollector.RequiresDataForCalculation(relativePath)) {
            int size = TkZstd.IsCompressed(input)
                ? TkZstd.GetDecompressedSize(input)
                : (int)input.Length;
            _resourceSizeCollector.Collect(size, relativePath, []);
            input.CopyTo(output);
            input.Dispose();
            return;
        }

        using RentedBuffer<byte> buffer = RentedBuffer<byte>.Allocate(input);
        Span<byte> raw = buffer.Span;

        if (!TkZstd.IsCompressed(raw)) {
            _resourceSizeCollector.Collect(raw.Length, relativePath, raw);
            output.Write(raw);
            input.Dispose();
            return;
        }

        using SpanOwner<byte> decompressed = SpanOwner<byte>.Allocate(TkZstd.GetDecompressedSize(raw));
        Span<byte> data = decompressed.Span;
        _rom.Zstd.Decompress(raw, data);
        _resourceSizeCollector.Collect(data.Length, relativePath, data);
        output.Write(raw);
        input.Dispose();
    }

    private void CopyVanillaPlaceholderToOutput(RentedBuffer<byte> input, TkChangelogEntry changelog)
    {
        if (changelog.RuntimeArchiveCanonicals.Count == 0) {
            throw new InvalidOperationException(
                "This should never have happened! Copying vanilla files into a mod is useless.");
        }

        _packFileCollector.Collect(changelog, input);
    }

    private void CopyMergedToOutput(in MemoryStream input, string relativePath, TkChangelogEntry changelog)
    {
        if (changelog.RuntimeArchiveCanonicals.Count != 0) {
            _packFileCollector.Collect(changelog, input);
            return;
        }

        CopyMergedToSimpleOutput(input, relativePath, changelog.Attributes, changelog.ZsDictionaryId);
    }

    internal void CopyMergedToSimpleOutput(in MemoryStream input, string relativePath,
        TkFileAttributes entryFileAttributes, int zsDictionaryId)
    {
        ArraySegment<byte> buffer = input.GetSpan();
        _resourceSizeCollector.Collect(buffer.Count, relativePath, buffer);

        using Stream output = _output.OpenWrite(
            Path.Combine("romfs", relativePath));

        if (entryFileAttributes.HasFlag(TkFileAttributes.HasZsExtension)) {
            using SpanOwner<byte> compressed = SpanOwner<byte>.Allocate(buffer.Count);
            Span<byte> result = compressed.Span;
            int compressedSize = _rom.Zstd.Compress(buffer, result, zsDictionaryId);
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
            .SelectMany(changelog => changelog.ChangelogFiles
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
        // Ensure the key changelog contains
        // every archive requesting this file
        group.Key.RuntimeArchiveCanonicals = group.SelectMany(x => x.Entry.ArchiveCanonicals).ToList();

        if (GetMerger(group.Key.Canonical) is ITkMerger merger) {
            return (
                Changelog: group.Key,
                Target: (Merger: merger,
                    Streams: group
                        .Where(changelog => changelog.Entry.Type != ChangelogEntryType.Placeholder)
                        .Select(changelog => changelog.Changelog.Source!.OpenRead(GetRelativeRomFsPath(changelog.Entry)))
                        .ToArray()
                )
            );
        }

        // TODO: Proper support for mixed version usage should
        // be implemented, however by always using the path of
        // the last entry, we can avoid explicitly handling it
        (TkChangelogEntry Entry, TkChangelog Changelog) last = group.Last();
        string relativeFilePath = GetRelativeRomFsPath(last.Entry);

        return (
            Changelog: group.Key,
            Target: group.Key.Type switch {
                ChangelogEntryType.Placeholder => Either<(ITkMerger Merger, Stream[] Streams), Stream>.Bottom,
                _ => last.Changelog.Source!.OpenRead(relativeFilePath)
            }
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
                ".pack" => _packMerger,
                ".bfarc" or ".bkres" or ".blarc" or ".genvb" or ".ta" => _sarcMerger,
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

        if (canon.Length > 8 && canon[..8] is "Sequence") {
            ReadOnlySpan<char> sequenceName = Path.GetFileNameWithoutExtension(canon);
            int targetVersion = _rom.SequenceVersions.TryGetValue(sequenceName, out string? version)
                ? GetBestVersion(int.Parse(version.AsSpan()[^3..]), entry.Versions)
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

        if (canon.Length > 5 && canon[..5] is "Logic") {
            ReadOnlySpan<char> logicName = Path.GetFileNameWithoutExtension(canon);
            int targetVersion = _rom.LogicVersions.TryGetValue(logicName, out string? version)
                ? GetBestVersion(int.Parse(version.AsSpan()[^3..]), entry.Versions)
                : entry.Versions[0];
            return Path.Combine("romfs", $"{entry.Canonical}{targetVersion}");
        }

        if (canon.Length > 2 && canon[..2] is "AI") {
            ReadOnlySpan<char> aiName = Path.GetFileNameWithoutExtension(canon);
            int targetVersion = _rom.AiVersions.TryGetValue(aiName, out string? version)
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