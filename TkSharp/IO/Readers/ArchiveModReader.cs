using System.Buffers;
using SharpCompress.Archives;
using TkSharp.Core;
using TkSharp.Core.IO.ModSources;
using TkSharp.Core.Models;
using TkSharp.Merging;

namespace TkSharp.IO.Readers;

public sealed class ArchiveModReader(ITkSystemProvider systemProvider, ITkRomProvider romProvider, ITkModReaderProvider readerProvider) : ITkModReader
{
    private static readonly SearchValues<string> ValidFoldersSearchValues = SearchValues.Create(
        ["romfs", "exefs", "cheats", "extras"], StringComparison.OrdinalIgnoreCase);

    private readonly ITkSystemProvider _systemProvider = systemProvider;
    private readonly ITkRomProvider _romProvider = romProvider;

    public async ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        if (context.Input is not string fileName || context.Stream is null) {
            return null;
        }

        using var archive = ArchiveFactory.OpenArchive(context.Stream);
        return await ReadArchiveMod(archive, fileName, context, _systemProvider, _romProvider, readerProvider, ct)
            .ConfigureAwait(false);
    }

    public bool IsKnownInput(object? input)
    {
        return input is string path &&
               Path.GetExtension(path.AsSpan()) is ".zip" or ".rar";
    }

    internal static async ValueTask<TkMod?> ReadArchiveMod(
        IArchive archive,
        string fileName,
        TkModContext context,
        ITkSystemProvider systemProvider,
        ITkRomProvider romProvider,
        ITkModReaderProvider readerProvider,
        CancellationToken ct = default)
    {
        var (roots, embeddedMod, isValid) = await LocateRoots(archive, readerProvider, fileName, ct)
            .ConfigureAwait(false);
        if (!isValid) {
            return null;
        }

        if (embeddedMod is not null) {
            return embeddedMod;
        }

        context.EnsureId();

        var writer = systemProvider.GetSystemWriter(context);
        var rom = romProvider.GetRom();
        var modSystemSource = systemProvider.GetSystemSource(context.Id.ToString());
        var modName = Path.GetFileNameWithoutExtension(fileName);

        if (roots.Count == 1) {
            var root = roots[0];
            ArchiveModSource source = new(archive, root.PathPrefix);
            TkChangelogBuilder builder = new(source, writer, rom, modSystemSource);
            var changelog = await builder.BuildAsync(ct).ConfigureAwait(false);

            return new TkMod {
                Id = context.Id,
                Name = modName,
                Changelog = changelog
            };
        }

        TkMod mod = new() {
            Id = context.Id,
            Name = modName,
            Changelog = new TkChangelog {
                BuilderVersion = 200,
                GameVersion = rom.GameVersion,
                Source = modSystemSource
            }
        };

        foreach (var (groupName, groupRoots) in GroupRoots(roots)) {
            TkModOptionGroup group = new() {
                Name = groupName,
                Type = OptionGroupType.Multi
            };

            foreach (var root in groupRoots) {
                TkModOption option = new() {
                    Name = root.OptionName
                };

                writer.SetRelativeFolder(option.Id.ToString());

                ArchiveModSource source = new(archive, root.PathPrefix);
                TkChangelogBuilder builder = new(
                    source, writer, rom, modSystemSource.GetRelative(option.Id.ToString())
                );
                option.Changelog = await builder.BuildAsync(ct).ConfigureAwait(false);
                group.Options.Add(option);
            }

            mod.OptionGroups.Add(group);
        }

        writer.SetRelativeFolder(string.Empty);

        return mod;
    }

    internal static async ValueTask<(IReadOnlyList<LocatedArchiveRoot> Roots, TkMod? EmbeddedMod, bool IsValid)> LocateRoots(
        IArchive archive, ITkModReaderProvider readerProvider, string fileName, CancellationToken ct = default)
    {
        foreach (var entry in archive.Entries) {
            if (entry.Key is not null
                && Path.GetExtension(entry.Key.AsSpan()) is ".tkcl"
                && readerProvider.GetReader(entry.Key) is { } reader) {
                await using var entryStream = entry.OpenEntryStream();
                await using MemoryStream tkclBuffer = new();
                await entryStream.CopyToAsync(tkclBuffer, ct);
                tkclBuffer.Position = 0;
                var embeddedMod = await reader.ReadMod(entry.Key, tkclBuffer, ct: ct)
                    .ConfigureAwait(false);
                if (embeddedMod is not null) {
                    return ([], embeddedMod, true);
                }
            }
        }

        Dictionary<string, string?> uniqueRoots = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries) {
            if (TryGetRootPrefix(entry, out var rootPrefix)) {
                uniqueRoots.TryAdd(rootPrefix ?? string.Empty, rootPrefix);
            }
        }

        if (uniqueRoots.Count == 0) {
            return ([], null, false);
        }

        var archiveBaseName = Path.GetFileNameWithoutExtension(fileName);
        var roots = uniqueRoots.Values
            .Select(prefix => {
                var (groupName, optionName) = GetGroupAndOptionName(prefix, archiveBaseName);
                return new LocatedArchiveRoot(prefix, groupName, optionName);
            })
            .ToList();

        return (roots, null, true);
    }

    internal readonly record struct LocatedArchiveRoot(string? PathPrefix, string GroupName, string OptionName);

    private static IEnumerable<(string GroupName, IReadOnlyList<LocatedArchiveRoot> Roots)> GroupRoots(
        IReadOnlyList<LocatedArchiveRoot> roots)
    {
        return roots
            .GroupBy(root => root.GroupName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Equals("Options", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                group.Key,
                (IReadOnlyList<LocatedArchiveRoot>)group
                    .OrderBy(root => root.OptionName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            ));
    }

    private static bool TryGetRootPrefix(IArchiveEntry entry, out string? rootPrefix)
    {
        rootPrefix = null;

        if (entry.Key is null) {
            return false;
        }

        var key = entry.Key.AsSpan();
        var normalizedKey = key[^1] is '/' or '\\' ? key[..^1] : key;

        if (normalizedKey.Length < 5) {
            return false;
        }

        ReadOnlySpan<char> normalizedKeyLowercase = normalizedKey
            .ToString()
            .ToLowerInvariant();

        if (normalizedKeyLowercase[..5] is "romfs" or "exefs"
            || normalizedKeyLowercase.Length >= 6 && normalizedKeyLowercase[..6] is "cheats" or "extras") {
            return true;
        }

        if (entry.IsDirectory) {
            switch (Path.GetFileName(normalizedKeyLowercase)) {
                case "romfs" or "exefs":
                    rootPrefix = NormalizeRootPrefix(normalizedKey[..^5]);
                    return true;
                case "cheats" or "extras":
                    rootPrefix = NormalizeRootPrefix(normalizedKey[..^6]);
                    return true;
            }

            return false;
        }

        if (normalizedKeyLowercase.IndexOfAny(ValidFoldersSearchValues) is var index && index is -1) {
            return false;
        }

        rootPrefix = NormalizeRootPrefix(normalizedKey[..index]);
        return true;
    }

    private static string NormalizeRootPrefix(ReadOnlySpan<char> prefix)
    {
        return prefix.ToString().Replace('\\', '/').TrimEnd('/');
    }

    private static (string GroupName, string OptionName) GetGroupAndOptionName(string? rootPrefix, string archiveBaseName)
    {
        if (string.IsNullOrEmpty(rootPrefix)) {
            return ("Options", archiveBaseName);
        }

        var normalized = rootPrefix.Replace('\\', '/').TrimEnd('/');
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash < 0) {
            return ("Options", normalized);
        }

        return (normalized[..lastSlash], normalized[(lastSlash + 1)..]);
    }
}