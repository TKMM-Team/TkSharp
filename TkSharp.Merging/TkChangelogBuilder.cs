using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ChangelogBuilders;

namespace TkSharp.Merging;

public class TkChangelogBuilder(
    ITkModSource source,
    ITkModWriter writer,
    ITkRom tk,
    ITkSystemSource? systemSource,
    TkChangelogBuilderFlags flags = default)
{
    private static ITkRom? _sessionRom;
    private static ITkRomProvider? _romProvider;

    // This is a bit sketchy and should probably be fixed
    // in a better way.
    // 
    // TL;DR static/unchecked calls to TkChangelogBuilder
    // need access to ITkRom, so this field is assigned whenever
    // a new TkChangelogBuilder is constructed (or creates a new one if needed)
    public static ITkRom SessionRom
        => _sessionRom ?? _romProvider?.GetRom()
            ?? throw new Exception("The changelog builder has not been statically initialized");

    private readonly ITkModSource _source = source;
    private readonly ITkModWriter _writer = writer;
    private readonly ITkRom _tk = InitSession(tk);
    private readonly Dictionary<string, TkChangelogEntry> _entries = [];

    private readonly TkChangelog _changelog = new() {
        BuilderVersion = 200,
        GameVersion = tk.GameVersion,
        Source = systemSource
    };

    public static void Init(ITkRomProvider tkRomProvider)
    {
        _romProvider = tkRomProvider;
    }

    private static ITkRom InitSession(ITkRom tk)
    {
        return _sessionRom = tk;
    }

    public async ValueTask<TkChangelog> BuildParallel(CancellationToken ct = default)
    {
        await Task.WhenAll(_source.Files.Select(file => Task.Run(() => BuildTarget(file.FilePath, file.Entry), ct)
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
        var path = TkPath.FromPath(file, _source.PathToRoot, out bool isInvalid);
        if (isInvalid) {
            return;
        }

        string canonical = path.Canonical.ToString();

        using var content = _source.OpenRead(entry);

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
        using (var output = _writer.OpenWrite(outputFilePath)) {
            content.CopyTo(output);
        }

        return;

    Build:
        if (GetChangelogBuilder(path) is not ITkChangelogBuilder builder) {
            AddChangelogMetadata(path, ref canonical, ChangelogEntryType.Copy, zsDictionaryId: -1, path.FileVersion);
            goto Copy;
        }

        if (path.Canonical.StartsWith("GameData/") && path.FileVersion != -1) {
            var versionedPath = $"GameData/GameDataList.Product.{path.FileVersion}.byml";
            using var testVanilla = _tk.GetVanilla(versionedPath, path.Attributes);
            
            if (testVanilla.IsEmpty) {
                TkLog.Instance.LogTrace(
                    "The target '{FileName}' was skipped because its version {FileVersion} does not correspond to the provided dump.", versionedPath, path.FileVersion);
                return;
            }
        }

        using var raw = RentedBuffer<byte>.Allocate(content);
        _ = content.Read(raw.Span);

        bool isZsCompressed = TkZstd.IsCompressed(raw.Span);

        using var decompressed = isZsCompressed
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

        using var vanilla
            = _tk.GetVanilla(canonical, path.Attributes);

        // Let the changelog builder handle
        // pack files without a vanilla file  
        if (vanilla.IsEmpty && !builder.CanProcessWithoutVanilla) {
            AddChangelogMetadata(path, ref canonical, ChangelogEntryType.Copy, zsDictionaryId, path.FileVersion);
            outputFilePath = Path.Combine(path.Root.ToString(), canonical);
            using var output = _writer.OpenWrite(outputFilePath);
            output.Write(raw.Span);
            return;
        }

        var parentAttributes = path.Attributes;
        bool isVanilla = !builder.Build(canonical, path, flags, decompressed.IsEmpty ? raw.Segment : decompressed.Segment, vanilla.Segment,
            (path, canon, archiveCanon, type) => {
                if (Path.GetFileName(canon).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                    TkLog.Instance.LogWarning("The target '{FileName}' was ignored due to incorrect characters in the file name", canon);
                    return Stream.Null;
                }
                
                AddChangelogMetadata(path, ref canon, type, zsDictionaryId, path.FileVersion,
                    // Force the parent attributes onto the entry for all parent archives
                    archiveCanon, archiveCanon is not null ? parentAttributes : null);
                string outputFile = Path.Combine(path.Root.ToString(), canon);
                return _writer.OpenWrite(outputFile);
            });
        builder.Dispose();

        if (isVanilla) {
            TkLog.Instance.LogTrace(
                "The target '{FileName}' was skipped because no changes were found from the vanilla file", canonical);
        }
    }

    public static ArraySegment<ArraySegment<byte>> CreateChangelogsExternal(string canonical, TkChangelogBuilderFlags flags, ArraySegment<byte> @base, RentedBuffers<byte> changelogs, TkFileAttributes attributes)
    {
        TkPath path = new(canonical, 100, attributes, "romfs", "");

        if (GetChangelogBuilder(path) is not ITkChangelogBuilder builder) {
            throw new InvalidOperationException(
                $"Target file {canonical} cannot be merged as a custom file because no changelog builder could be found.");
        }

        int index = -1;
        var result = new ArraySegment<byte>[changelogs.Count];

        foreach (var entry in changelogs) {
            using MemoryStream output = new();
            TkPath pathIteratorStackInstance = new(canonical, 100, attributes, "romfs", "");

            // ReSharper disable once AccessToDisposedClosure
            if (builder.Build(canonical, pathIteratorStackInstance, flags, entry.Segment, @base, (_, _, _, _) => output)) {
                // Copy the buffer because output
                // is disposed before this is used
                result[++index] = output.ToArray();
            }
            
            builder.Dispose();
        }

        return new ArraySegment<ArraySegment<byte>>(result, 0, ++index);
    }

    private void AddChangelogMetadata(in TkPath path, ref string canonical, ChangelogEntryType type, int zsDictionaryId,
        int fileVersion, string? archiveCanonical = null, TkFileAttributes? archiveAttributes = null)
    {
        if (path.Canonical.Length > 4 && path.Canonical[..4] is "Mals") {
            _changelog.MalsFiles.Add(canonical);
            return;
        }

        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, canonical, out bool exists);
        if (!exists || entry is null) {
            entry = new TkChangelogEntry(
                canonical, type, archiveAttributes ?? path.Attributes, zsDictionaryId
            );
        }

        if (fileVersion != -1) {
            entry.Versions.Add(fileVersion);
            canonical += fileVersion.ToString();
        }

        if (archiveCanonical is not null) {
            entry.ArchiveCanonicals.Add(archiveCanonical);
        }

        // Make sure that the last occurence type is used
        // 
        // This should never be different, but mod devs
        // often make the mistake of having a placeholder
        // and changelog of the same file.
        entry.Type = type;
    }

    internal static ITkChangelogBuilder? GetChangelogBuilder(in TkPath path)
    {
        if (path.Extension is ".pack") {
            // If _sessionRom is null, then SessionRom will
            // return a new instance just for this call.
            return new PackChangelogBuilder(SessionRom, disposeTkRom: _sessionRom is null);
        }
        
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
            { Extension: ".bfarc" or ".bkres" or ".blarc" or ".genvb" or ".sarc" or ".ta" } => SarcChangelogBuilder.Instance,
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