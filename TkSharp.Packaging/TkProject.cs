using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CommunityToolkit.HighPerformance.Buffers;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO;
using TkSharp.Core.IO.ModSources;
using TkSharp.Core.Models;
using TkSharp.Merging;
using TkSharp.Packaging.IO.Serialization;
using TkSharp.Packaging.IO.Writers;

namespace TkSharp.Packaging;

public partial class TkProject(string folderPath) : ObservableObject
{
    [ObservableProperty]
    private string _folderPath = folderPath;

    [ObservableProperty]
    private TkMod _mod = new();

    [ObservableProperty]
    private TkChangelogFlags _flags = new();

    private readonly Dictionary<TkItem, string> _itemPathLookup = [];

    public void Refresh()
    {
        Mod.OptionGroups.Clear();
        TkProjectManager.LoadProjectOptionsFromFolder(this);
    }

    public async ValueTask Package(Stream output, ITkRom rom, CancellationToken ct = default)
    {
        TkLog.Instance.LogInformation("Packaging '{ModName}'", Mod.Name);

        ArchiveModWriter writer = new();
        await Build(writer, rom, ct: ct);

        using MemoryStream contentArchiveOutput = new();
        writer.Compile(contentArchiveOutput);

        TkPackWriter.Write(output, Mod, contentArchiveOutput.GetSpan());

        TkLog.Instance.LogInformation("Packaged: '{ModName}'", Mod.Name);
    }

    public async ValueTask Build(ITkModWriter writer, ITkRom rom, ITkSystemSource? systemSource = null, CancellationToken ct = default)
    {
        PackThumbnails(writer);

        TkChangelogBuilderFlags flags = Flags.GetBuilderFlags();

        FolderModSource source = new(FolderPath);
        Mod.Changelog = await Build(Mod, source, writer, rom, systemSource, flags, ct);

        foreach (TkModOption option in Mod.OptionGroups.SelectMany(group => group.Options)) {
            if (!TryGetPath(option, out string? optionPath)) {
                continue;
            }

            FolderModSource optionSource = new(optionPath);

            string id = option.Id.ToString();
            writer.SetRelativeFolder(id);
            option.Changelog = await Build(
                option, optionSource, writer, rom, systemSource?.GetRelative(id), flags, ct);
        }

        TkLog.Instance.LogInformation("Build completed");
    }

    private static async ValueTask<TkChangelog> Build(TkStoredItem item, ITkModSource source, ITkModWriter writer,
        ITkRom rom, ITkSystemSource? systemSource = null, TkChangelogBuilderFlags flags = default, CancellationToken ct = default)
    {
        TkLog.Instance.LogInformation("Building: '{ItemName}'", item.Name);

        TkChangelogBuilder builder = new(source, writer, rom, systemSource, flags);
        TkChangelog result = await builder.BuildAsync(ct)
            .ConfigureAwait(false);

        TkLog.Instance.LogInformation("Built: '{ItemName}'", item.Name);
        return result;
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        string projectFilePath = Path.Combine(FolderPath, ".tkproj");
        using FileStream output = File.Create(projectFilePath);
        JsonSerializer.Serialize(output, Mod);

        SaveOptionsGroups();
    }

    private void SaveOptionsGroups()
    {
        foreach (TkModOptionGroup group in Mod.OptionGroups) {
            if (!TryGetPath(group, out string? groupFolderPath)) {
                continue;
            }

            string metadataFilePath = Path.Combine(groupFolderPath, "info.json");
            using FileStream fs = File.Create(metadataFilePath);
            JsonSerializer.Serialize(fs, group);

            SaveOptions(group);
        }
    }

    private void SaveOptions(TkModOptionGroup group)
    {
        foreach (TkModOption option in group.Options) {
            if (!TryGetPath(option, out string? optionPath)) {
                continue;
            }

            string metadataFilePath = Path.Combine(optionPath, "info.json");
            using FileStream fs = File.Create(metadataFilePath);
            JsonSerializer.Serialize(fs, option);
        }
    }

    public void RegisterItem(TkItem option, string sourceFolderPath)
    {
        _itemPathLookup[option] = sourceFolderPath;
    }

    public bool TryGetPath(TkItem item, [MaybeNullWhen(false)] out string path)
    {
        if (_itemPathLookup.TryGetValue(item, out string? folderPath) && Directory.Exists(folderPath)) {
            path = folderPath;
            return true;
        }

        TkLog.Instance.LogWarning("""
            The source folder for the item '{ItemName}' could not be found.
            The folder may have been moved or deleted, this item will not be part of the output.
            """, item.Name);

        path = null;
        return false;
    }

    private void PackThumbnails(ITkModWriter writer)
    {
        PackThumbnail(Mod, writer);

        foreach (TkModOptionGroup group in Mod.OptionGroups) {
            PackThumbnail(group, writer);

            foreach (TkModOption option in group.Options) {
                PackThumbnail(option, writer);
            }
        }
    }

    private static void PackThumbnail(TkItem item, ITkModWriter writer)
    {
        if (item.Thumbnail is null) {
            return;
        }

        if (!File.Exists(item.Thumbnail.ThumbnailPath)) {
            item.Thumbnail = null;
            return;
        }

        string thumbnailFilePath = Path.Combine("img", Ulid.NewUlid().ToString());
        item.Thumbnail.RelativeThumbnailPath = thumbnailFilePath;

        using FileStream fs = File.OpenRead(item.Thumbnail.ThumbnailPath);
        int size = (int)fs.Length;
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        fs.ReadExactly(buffer.Span);

        using Stream output = writer.OpenWrite(thumbnailFilePath);
        output.Write(buffer.Span);
    }

    public async ValueTask PackageOptimizer(Stream output, CancellationToken ct = default)
    {
        TkLog.Instance.LogInformation("Packaging TotK Optimizer from project '{ModName}'", Mod.Name);

        ArchiveModWriter writer = new();
        await BuildMetadata(writer, ct: ct);

        using MemoryStream contentArchiveOutput = new();
        writer.Compile(contentArchiveOutput);

        TkPackWriter.Write(output, Mod, contentArchiveOutput.GetSpan());

        TkLog.Instance.LogInformation("Packaged '{ModName}'", Mod.Name);
    }

    public async ValueTask BuildMetadata(ITkModWriter writer, ITkSystemSource? systemSource = null, CancellationToken ct = default)
    {
        PackThumbnails(writer);
        
        TkChangelogBuilderFlags flags = Flags.GetBuilderFlags();

        FolderModSource source = new(FolderPath);
        var nullRom = new NullTkRom();
        TkChangelogBuilder builder = new(source, writer, nullRom, systemSource, flags);
        Mod.Changelog = await builder.BuildAsync(ct);

        foreach (TkModOption option in Mod.OptionGroups.SelectMany(group => group.Options)) {
            if (!TryGetPath(option, out string? optionPath)) {
                continue;
            }

            FolderModSource optionSource = new(optionPath);
            string id = option.Id.ToString();
            writer.SetRelativeFolder(id);

            TkChangelogBuilder optionBuilder = new(optionSource, writer, nullRom,
                systemSource?.GetRelative(id), flags);
            option.Changelog = await optionBuilder.BuildAsync(ct);
        }

        TkLog.Instance.LogInformation("Metadata build completed");
    }
}