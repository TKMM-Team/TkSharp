using System.Runtime.CompilerServices;
using ReverseMarkdown;
using TkSharp.Core;
using TkSharp.Core.Models;
using TkSharp.Extensions.GameBanana.Helpers;

namespace TkSharp.Extensions.GameBanana.Readers;

public sealed class GameBananaModReader(ITkModReaderProvider readerProvider) : ITkModReader
{
    private readonly ITkModReaderProvider _readerProvider = readerProvider;

    public async ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        switch (context.Input) {
            case GameBananaFile file:
                return await ReadFrom(context, file.DownloadUrl, file.Id, file, ct);
            case ValueTuple<GameBananaMod, GameBananaFile> pair:
                return await ReadFrom(context, pair.Item1, pair.Item2, ct);
        }

        if (context.Input is not string arg) {
            return null;
        }

        if (!GbUrlHelper.TryGetId(arg, out var id)) {
            return null;
        }

        if (arg.Contains("/mods/")) {
            return await ReadFrom(context, id, ct: ct);
        }

        return await ReadFrom(context, arg, id, ct: ct);
    }

    public bool IsKnownInput(object? input)
    {
        return input is GameBananaFile or ValueTuple<GameBananaMod, GameBananaFile>
               || (input is string arg && (
                   arg.Contains("gamebanana.com/mods/") || arg.Contains("gamebanana.com/dl/")
               ) && GbUrlHelper.TryGetId(arg, out _));
    }
    
    public async ValueTask<TkMod?> ReadFrom(TkModContext context, long modId, long fileId, CancellationToken ct = default)
    {
        if (await GameBanana.Get<GameBananaFile>(
                $"File/{fileId}", GameBananaModJsonContext.Default.GameBananaFile, ct) is not { } file) {
            return null;
        }
        
        var gbMod = await GameBanana.GetMod(modId, ct);
        return await ReadFrom(context, gbMod, file, ct);
    }
    
    public async ValueTask<TkMod?> ReadFrom(TkModContext context, long modId, GameBananaFile? target = null, CancellationToken ct = default)
    {
        var gbMod = await GameBanana.GetMod(modId, ct);
        return await ReadFrom(context, gbMod, target, ct);
    }
    
    public async ValueTask<TkMod?> ReadFrom(TkModContext context, GameBananaMod? gbMod, GameBananaFile? targetFile = null, CancellationToken ct = default)
    {
        targetFile ??= gbMod?.Files
            .FirstOrDefault(static file => file.IsTkcl);
        targetFile ??= gbMod?.Files
            .FirstOrDefault(file => _readerProvider.CanRead(file.Name));
        
        if (targetFile is null || gbMod is null) {
            return null;
        }

        var mod = await ReadFrom(context, targetFile.DownloadUrl, targetFile.Id, targetFile, ct);

        if (mod is null) {
            return null;
        }

        mod.Name = gbMod.Name;
        mod.Author = gbMod.Submitter.Name;
        mod.Description = $"""
            *[Game Banana Mod Page ->](https://gamebanana.com/mods/{gbMod.Id})*

            {
                new Converter(new Config {
                    GithubFlavored = true,
                    ListBulletChar = '*',
                    UnknownTags = Config.UnknownTagsOption.Bypass}).Convert(gbMod.Text)
            }
            """;
        mod.Thumbnail = new TkThumbnail {
            ThumbnailPath = gbMod.Media.Images.First() switch {
                var image => $"{image.BaseUrl}/{image.File}"
            }
        };
        mod.Version = string.IsNullOrWhiteSpace(gbMod.Version) ? "1.0.0" : gbMod.Version;

        foreach (var author in gbMod.Credits.SelectMany(group => group.Authors)) {
            mod.Contributors.Add(new TkModContributor(author.Name, author.Role));
        }

        return mod;
    }

    public async ValueTask<TkMod?> ReadFrom(TkModContext context, string fileUrl, long fileId, GameBananaFile? target = null, CancellationToken ct = default)
    {
        target ??= await GameBanana.Get<GameBananaFile>($"File/{fileId}", GameBananaModJsonContext.Default.GameBananaFile, ct);

        if (target is null) {
            return null;
        }

        var fileIdAsInt = (Int128)fileId;

        var reader = _readerProvider.GetReader(target.Name);
        context.EnsureId(
            Unsafe.As<Int128, Ulid>(ref fileIdAsInt)
        );

        var data = await DownloadHelper.DownloadAndVerify(
            fileUrl, Convert.FromHexString(target.Checksum), ct: ct);

        await using MemoryStream ms = new(data);
        return reader?.ReadMod(target.Name, ms, context, ct) switch {
            { } result => await result,
            _ => null
        };
    }
}