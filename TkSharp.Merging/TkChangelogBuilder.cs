using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ChangelogBuilders;

namespace TkSharp.Merging;

public class TkChangelogBuilder(ITkModSource source, ITkModWriter writer, ITkRom tk,
    ITkSystemSource? systemSource, TkChangelogBuilderFlags flags = default)
{
    private readonly ITkModSource _source = source;
    private readonly ITkModWriter _writer = writer;
    private readonly ITkRom _tk = tk;
    private readonly Dictionary<string, TkChangelogEntry> _entries = [];

    private readonly TkChangelog _changelog = new() {
        BuilderVersion = 100,
        GameVersion = tk.GameVersion,
        Source = systemSource
    };

    public async ValueTask<TkChangelog> BuildParallel(CancellationToken ct = default)
    {
        await Task.WhenAll(_source.Files.Select(
                file => Task.Run(() => BuildTarget(file.FilePath, file.Entry), ct)
            ))
            .ConfigureAwait(false);

        InsertEntries();
        return _changelog;
    }

    public Task<TkChangelog> BuildAsync(CancellationToken ct = default)
    {
        return Task.Run(Build, ct);
    }

    public TkChangelog Build()
    {
        foreach ((string file, object entry) in _source.Files) {
            BuildTarget(file, entry);
        }

        InsertEntries();
        return _changelog;
    }

    private void BuildTarget(string file, object entry)
    {
        TkPath path = TkPath.FromPath(file, _source.PathToRoot, out bool isInvalid);
        if (isInvalid) {
            return;
        }

        string canonical = path.Canonical.ToString();

        using Stream content = _source.OpenRead(entry);

        switch (path) {
            case { Root: "exefs", Extension: ".ips" }:
                if (TkPatch.FromIps(content, path.Canonical[..^4].ToString()) is TkPatch patch) {
                    _changelog.PatchFiles.Add(patch);
                }

                return;
            case { Root: "exefs", Extension: ".pchtxt" }:
                if (TkPatch.FromPchTxt(content) is TkPatch patchFromPchtxt) {
                    _changelog.PatchFiles.Add(patchFromPchtxt);
                }

                return;
            case { Root: "exefs", Canonical.Length: 7 } when path.Canonical[..6] is "subsdk":
                _changelog.SubSdkFiles.Add(canonical);
                goto Copy;
            case { Root: "exefs" }:
                _changelog.ExeFiles.Add(canonical);
                goto Copy;
            case { Root: "extras" }:
                goto Copy;
            case { Root: "cheats" }:
                if (TkCheat.FromText(content, Path.GetFileNameWithoutExtension(canonical)) is var cheat and { Count: > 0 }) {
                    _changelog.CheatFiles.Add(cheat);
                }

                return;
            case { Extension: ".rsizetable" } or { Canonical: "desktop.ini" }:
                return;
        }

        goto Build;

    Copy:
        string outputFilePath = Path.Combine(path.Root.ToString(), canonical);
        // ReSharper disable once ConvertToUsingDeclaration
        using (Stream output = _writer.OpenWrite(outputFilePath)) {
            content.CopyTo(output);
        }

        return;

    Build:
        if (GetChangelogBuilder(path) is not ITkChangelogBuilder builder) {
            AddChangelogMetadata(path, ref canonical, ChangelogEntryType.Copy, zsDictionaryId: -1, path.FileVersion);
            goto Copy;
        }

        using RentedBuffer<byte> raw = RentedBuffer<byte>.Allocate(content);
        _ = content.Read(raw.Span);

        bool isZsCompressed = TkZstd.IsCompressed(raw.Span);

        using RentedBuffer<byte> decompressed = isZsCompressed
            ? RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(raw.Span))
            : default;

        int zsDictionaryId = -1;
        if (isZsCompressed) {
            _tk.Zstd.Decompress(raw.Span, decompressed.Span, out zsDictionaryId);
        }

        if (_tk.IsVanilla(path.Canonical, decompressed.Span, path.FileVersion)) {
            TkLog.Instance.LogTrace(
                "The target '{FileName}' was skipped because the file is byte-perfect with the vanilla file", canonical);
            return;
        }

        using RentedBuffer<byte> vanilla
            = _tk.GetVanilla(canonical, path.Attributes);

        if (vanilla.IsEmpty) {
            AddChangelogMetadata(path, ref canonical, ChangelogEntryType.Copy, zsDictionaryId, path.FileVersion);
            outputFilePath = Path.Combine(path.Root.ToString(), canonical);
            using Stream output = _writer.OpenWrite(outputFilePath);
            output.Write(raw.Span);
            return;
        }

        bool isVanilla = !builder.Build(canonical, path, flags, decompressed.IsEmpty ? raw.Segment : decompressed.Segment, vanilla.Segment, (path, canon) => {
            AddChangelogMetadata(path, ref canon, ChangelogEntryType.Changelog, zsDictionaryId, path.FileVersion);
            string outputFile = Path.Combine(path.Root.ToString(), canon);
            return _writer.OpenWrite(outputFile);
        });

        if (isVanilla) {
            TkLog.Instance.LogTrace(
                "The target '{FileName}' was skipped because no changes were found from the vanilla file", canonical);
        }
    }

    public static IEnumerable<ArraySegment<byte>> CreateChangelogsExternal(string canonical, TkChangelogBuilderFlags flags, ArraySegment<byte> @base, IEnumerable<ArraySegment<byte>> changelogs, TkFileAttributes attributes)
    {
        TkPath path = new(canonical, 100, attributes, "romfs", "");

        if (GetChangelogBuilder(path) is not ITkChangelogBuilder builder) {
            throw new InvalidOperationException(
                $"Target file {canonical} cannot be merged as a custom file because no changelog builder could be found.");
        }

        // ReSharper disable once AccessToDisposedClosure
        foreach (ArraySegment<byte> changelog in changelogs) {
            using MemoryStream output = new();
            TkPath pathIteratorStackInstance = new(canonical, 100, attributes, "romfs", "");
            builder.Build(canonical, pathIteratorStackInstance, flags, changelog, @base, (_, _) => output);
            yield return output.GetSpan();
        }
    }

    private void AddChangelogMetadata(in TkPath path, ref string canonical, ChangelogEntryType type, int zsDictionaryId, int fileVersion)
    {
        if (path.Canonical.Length > 4 && path.Canonical[..4] is "Mals") {
            _changelog.MalsFiles.Add(canonical);
            return;
        }

        ref TkChangelogEntry? entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, canonical, out bool exists);
        if (!exists || entry is null) {
            entry = new TkChangelogEntry(
                canonical, type, path.Attributes, zsDictionaryId
            );
        }

        if (fileVersion != -1) {
            entry.Versions.Add(fileVersion);
            canonical += fileVersion.ToString();
        }
    }

    internal static ITkChangelogBuilder? GetChangelogBuilder(in TkPath path)
    {
        return path switch {
            { Canonical: "GameData/GameDataList.Product.byml" } => GameDataChangelogBuilder.Instance,
            { Canonical: "RSDB/Tag.Product.rstbl.byml" } => RsdbTagChangelogBuilder.Instance,
            { Canonical: "RSDB/GameSafetySetting.Product.rstbl.byml" } => RsdbRowChangelogBuilder.NameHash,
            { Canonical: "RSDB/RumbleCall.Product.rstbl.byml" or "RSDB/UIScreen.Product.rstbl.byml" } => RsdbRowChangelogBuilder.Name,
            { Canonical: "RSDB/TagDef.Product.rstbl.byml" } => RsdbRowChangelogBuilder.FullTagId,
            {
                Canonical: "RSDB/ActorInfo.Product.rstbl.byml" or
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
                "RSDB/XLinkPropertyTableList.Product.rstbl.byml"
            } => RsdbRowChangelogBuilder.RowId,
            { Extension: ".msbt" } => MsbtChangelogBuilder.Instance,
            { Extension: ".bfarc" or ".bkres" or ".blarc" or ".genvb" or ".pack" or ".sarc" or ".ta" } => SarcChangelogBuilder.Instance,
            { Extension: ".bgyml" } => BymlChangelogBuilder.Instance,
            { Extension: ".byml" } when path.Canonical[..4] is not "RSDB" && path.Canonical[..8] is not "GameData" => BymlChangelogBuilder.Instance,
            _ => null
        };
    }
    
    private void InsertEntries()
    {
        _changelog.ChangelogFiles.AddRange(_entries.Values);
    }
}