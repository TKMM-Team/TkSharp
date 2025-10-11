using System.Buffers;
using SharpCompress.Archives;
using TkSharp.Core;
using TkSharp.Core.IO.ModSources;
using TkSharp.Core.Models;
using TkSharp.Merging;

namespace TkSharp.IO.Readers;

public sealed class ArchiveModReader(ITkSystemProvider systemProvider, ITkRomProvider romProvider, ITkModReaderProvider readerProvider) : ITkModReader
{
    private static readonly SearchValues<string> _validFoldersSearchValues = SearchValues.Create(
        ["romfs", "exefs", "cheats", "extras"], StringComparison.OrdinalIgnoreCase);

    private readonly ITkSystemProvider _systemProvider = systemProvider;
    private readonly ITkRomProvider _romProvider = romProvider;

    public async ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        if (context.Input is not string fileName || context.Stream is null) {
            return null;
        }

        using var archive = ArchiveFactory.Open(context.Stream);
        (string? root, var embeddedMod, bool hasValidRoot) = await LocateRoot(archive, readerProvider);
        if (!hasValidRoot) {
            return null;
        }

        if (embeddedMod is not null) {
            return embeddedMod;
        }

        context.EnsureId();

        ArchiveModSource source = new(archive, root);
        var writer = _systemProvider.GetSystemWriter(context);

        TkChangelogBuilder builder = new(source, writer, _romProvider.GetRom(),
            _systemProvider.GetSystemSource(context.Id.ToString())
        );

        var changelog = await builder.BuildAsync(ct)
            .ConfigureAwait(false);

        return new TkMod {
            Id = context.Id,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Changelog = changelog
        };
    }

    public bool IsKnownInput(object? input)
    {
        return input is string path &&
               Path.GetExtension(path.AsSpan()) is ".zip" or ".rar";
    }

    internal static async ValueTask<(string? root, TkMod? embeddedMod, bool result)> LocateRoot(IArchive archive, ITkModReaderProvider readerProvider)
    {
        (string? Root, TkMod? Embedded, bool Result) result = (null, null, false);

        foreach (var entry in archive.Entries) {
            if (entry.Key is not null
                && Path.GetExtension(entry.Key.AsSpan()) is ".tkcl"
                && readerProvider.GetReader(entry.Key) is ITkModReader reader) {
                await using var entryStream = entry.OpenEntryStream();
                result.Embedded = await reader.ReadMod(entry.Key, entryStream);
            }

            if (result.Embedded is not null) {
                return result with { Result = true };
            }
        }

        foreach (var entry in archive.Entries) {
            var key = entry.Key.AsSpan();
            var normalizedKey = key[^1] is '/' or '\\' ? key[..^1] : key;

            if (normalizedKey.Length < 5) {
                continue;
            }

            ReadOnlySpan<char> normalizedKeyLowercase = normalizedKey
                .ToString()
                .ToLowerInvariant();

            if (normalizedKeyLowercase[..5] is "romfs" or "exefs" || normalizedKeyLowercase[..6] is "cheats" or "extras") {
                result.Root = null;
                return result with { Result = true };
            }

            if (entry.IsDirectory) {
                switch (Path.GetFileName(normalizedKeyLowercase)) {
                    case "romfs" or "exefs":
                        result.Root = normalizedKey[..^5].ToString();
                        return result with { Result = true };
                    case "cheats" or "extras":
                        result.Root = normalizedKey[..^6].ToString();
                        return result with { Result = true };
                }

                continue;
            }

            if (normalizedKeyLowercase.IndexOfAny(_validFoldersSearchValues) is var index && index is -1) {
                continue;
            }

            result.Root = normalizedKey[..index].ToString();
            return result with { Result = true };
        }

        return result;
    }
}