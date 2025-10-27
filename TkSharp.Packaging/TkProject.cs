#pragma warning disable CS0657 // Not a valid attribute location for this declaration

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [property: JsonIgnore]
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

        var flags = Flags.GetBuilderFlags();

        FolderModSource source = new(FolderPath);
        Mod.Changelog = await Build(Mod, source, writer, rom, systemSource, flags, ct);

        foreach (var option in Mod.OptionGroups.SelectMany(group => group.Options)) {
            if (!TryGetPath(option, out var optionPath)) {
                continue;
            }

            FolderModSource optionSource = new(optionPath);

            var id = option.Id.ToString();
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
        var result = await builder.BuildAsync(ct)
            .ConfigureAwait(false);

        TkLog.Instance.LogInformation("Built: '{ItemName}'", item.Name);
        return result;
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        var projectFilePath = Path.Combine(FolderPath, ".tkproj");
        using var output = File.Create(projectFilePath);
        JsonSerializer.Serialize(output, this);

        SaveOptionsGroups();
    }

    private void SaveOptionsGroups()
    {
        foreach (var group in Mod.OptionGroups) {
            if (!TryGetPath(group, out var groupFolderPath)) {
                continue;
            }

            var metadataFilePath = Path.Combine(groupFolderPath, "info.json");
            using var fs = File.Create(metadataFilePath);
            JsonSerializer.Serialize(fs, group);

            SaveOptions(group);
        }
    }

    private void SaveOptions(TkModOptionGroup group)
    {
        foreach (var option in group.Options) {
            if (!TryGetPath(option, out var optionPath)) {
                continue;
            }

            var metadataFilePath = Path.Combine(optionPath, "info.json");
            using var fs = File.Create(metadataFilePath);
            JsonSerializer.Serialize(fs, option);
        }
    }

    public void RegisterItem(TkItem option, string sourceFolderPath)
    {
        _itemPathLookup[option] = sourceFolderPath;
    }

    public bool TryGetPath(TkItem item, [MaybeNullWhen(false)] out string path)
    {
        if (_itemPathLookup.TryGetValue(item, out var folderPath) && Directory.Exists(folderPath)) {
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
        SetThumbnailIfNull(Mod, FolderPath);

        PackThumbnail(Mod, writer);

        foreach (var group in Mod.OptionGroups) {
            if (TryGetPath(group, out var groupPath)) {
                SetThumbnailIfNull(group, groupPath);
            }

            PackThumbnail(group, writer);

            foreach (var option in group.Options) {
                if (TryGetPath(option, out var optionPath)) {
                    SetThumbnailIfNull(option, optionPath);
                }

                PackThumbnail(option, writer);
            }
        }
    }

    private static void SetThumbnailIfNull(TkItem item, string folderPath)
    {
        if (item.Thumbnail?.ThumbnailPath != null && File.Exists(item.Thumbnail.ThumbnailPath)) {
            return;
        }

        var thumbnailPng = Path.Combine(folderPath, "thumbnail.png");
        var thumbnailJpg = Path.Combine(folderPath, "thumbnail.jpg");
        
        if (File.Exists(thumbnailPng)) {
            item.Thumbnail = new TkThumbnail { ThumbnailPath = thumbnailPng };
        }
        else if (File.Exists(thumbnailJpg)) {
            item.Thumbnail = new TkThumbnail { ThumbnailPath = thumbnailJpg };
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

        var thumbnailFilePath = Path.Combine("img", Ulid.NewUlid().ToString());
        item.Thumbnail.RelativeThumbnailPath = thumbnailFilePath;

        using var fs = File.OpenRead(item.Thumbnail.ThumbnailPath);
        var size = (int)fs.Length;
        using var buffer = SpanOwner<byte>.Allocate(size);
        fs.ReadExactly(buffer.Span);

        using var output = writer.OpenWrite(thumbnailFilePath);
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
        
        var flags = Flags.GetBuilderFlags();

        FolderModSource source = new(FolderPath);
        var nullRom = new NullTkRom();
        TkChangelogBuilder builder = new(source, writer, nullRom, systemSource, flags);
        Mod.Changelog = await builder.BuildAsync(ct);

        foreach (var option in Mod.OptionGroups.SelectMany(group => group.Options)) {
            if (!TryGetPath(option, out var optionPath)) {
                continue;
            }

            FolderModSource optionSource = new(optionPath);
            var id = option.Id.ToString();
            writer.SetRelativeFolder(id);

            TkChangelogBuilder optionBuilder = new(optionSource, writer, nullRom,
                systemSource?.GetRelative(id), flags);
            option.Changelog = await optionBuilder.BuildAsync(ct);
        }

        TkLog.Instance.LogInformation("Metadata build completed");
    }
}