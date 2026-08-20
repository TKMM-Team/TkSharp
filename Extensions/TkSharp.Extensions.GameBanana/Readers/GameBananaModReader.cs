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
            case ValueTuple<GameBananaSubmission, GameBananaFile> pair:
                return await ReadFrom(context, pair.Item1, pair.Item2, ct);
        }

        if (context.Input is not string arg) {
            return null;
        }

        if (!GbUrlHelper.TryGetId(arg, out var id)) {
            return null;
        }

        if (arg.Contains("/mods/", StringComparison.OrdinalIgnoreCase)) {
            return await ReadFrom(context, id, GameBananaSubmissionType.Mod, ct: ct);
        }

        if (arg.Contains("/wips/", StringComparison.OrdinalIgnoreCase)) {
            return await ReadFrom(context, id, GameBananaSubmissionType.Wip, ct: ct);
        }

        return await ReadFrom(context, arg, id, ct: ct);
    }

    public bool IsKnownInput(object? input)
    {
        return input is GameBananaFile or ValueTuple<GameBananaSubmission, GameBananaFile>
               || (input is string arg && (
                   arg.Contains("gamebanana.com/mods/", StringComparison.OrdinalIgnoreCase)
                   || arg.Contains("gamebanana.com/wips/", StringComparison.OrdinalIgnoreCase)
                   || arg.Contains("gamebanana.com/dl/", StringComparison.OrdinalIgnoreCase)
               ) && GbUrlHelper.TryGetId(arg, out _));
    }

    public async ValueTask<TkMod?> ReadFrom(TkModContext context, long submissionId, long fileId,
        GameBananaSubmissionType type = GameBananaSubmissionType.Mod, CancellationToken ct = default)
    {
        if (await GameBanana.Get(
                $"File/{fileId}", GameBananaSubmissionJsonContext.Default.GameBananaFile, ct) is not { } file) {
            return null;
        }

        var submission = await GameBanana.GetSubmission(submissionId, type, ct);
        return await ReadFrom(context, submission, file, ct);
    }

    public async ValueTask<TkMod?> ReadFrom(TkModContext context, long submissionId,
        GameBananaSubmissionType type = GameBananaSubmissionType.Mod, GameBananaFile? target = null,
        CancellationToken ct = default)
    {
        var submission = await GameBanana.GetSubmission(submissionId, type, ct);
        return await ReadFrom(context, submission, target, ct);
    }

    public async ValueTask<TkMod?> ReadFrom(TkModContext context, GameBananaSubmission? submission,
        GameBananaFile? targetFile = null, CancellationToken ct = default)
    {
        targetFile ??= submission?.Files
            .FirstOrDefault(static file => file.IsRecommended);
        targetFile ??= submission?.Files
            .FirstOrDefault(file => _readerProvider.CanRead(file.Name));
        targetFile ??= submission?.ArchivedFiles
            .FirstOrDefault(static file => file.IsRecommended);
        targetFile ??= submission?.ArchivedFiles
            .FirstOrDefault(file => _readerProvider.CanRead(file.Name));

        if (targetFile is null || submission is null) {
            return null;
        }

        var mod = await ReadFrom(context, targetFile.DownloadUrl, targetFile.Id, targetFile, ct);

        if (mod is null) {
            return null;
        }

        var gbPageLink = $"*[Game Banana Page ->]({submission.ProfileUrl})*";

        if (context.IsEmbeddedTkcl) {
            mod.Description = $"{gbPageLink}\n\n{mod.Description}";
            return mod;
        }

        mod.Name = submission.Name;
        mod.Author = submission.Submitter.Name;
        mod.Description = $"""
            {gbPageLink}

            {
                new Converter(new Config {
                    GithubFlavored = true,
                    ListBulletChar = '*',
                    UnknownTags = Config.UnknownTagsOption.Bypass}).Convert(submission.Text)
            }
            """;
        mod.Thumbnail = new TkThumbnail {
            ThumbnailPath = submission.Media.Images.First() switch {
                var image => $"{image.BaseUrl}/{image.File}"
            }
        };
        mod.Version = string.IsNullOrWhiteSpace(submission.Version) ? "1.0.0" : submission.Version;

        foreach (var author in submission.Credits.SelectMany(group => group.Authors)) {
            mod.Contributors.Add(new TkModContributor(author.Name, author.Role));
        }

        return mod;
    }

    public async ValueTask<TkMod?> ReadFrom(TkModContext context, string fileUrl, long fileId,
        GameBananaFile? target = null, CancellationToken ct = default)
    {
        target ??= await GameBanana.Get($"File/{fileId}", GameBananaSubmissionJsonContext.Default.GameBananaFile, ct);

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