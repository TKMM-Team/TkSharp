using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using TkSharp.Core;
using TkSharp.Core.IO.ModSources;
using TkSharp.Core.Models;
using TkSharp.Merging;

namespace TkSharp.IO.Readers;

public sealed class SevenZipModReader(ITkSystemProvider systemProvider, ITkRomProvider romProvider, ITkModReaderProvider readerProvider) : ITkModReader
{
    private readonly ITkSystemProvider _systemProvider = systemProvider;
    private readonly ITkRomProvider _romProvider = romProvider;

    public async ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        if (context.Input is not string fileName || context.Stream is null) {
            return null;
        }
        
        using var archive = SevenZipArchive.Open(context.Stream);
        (var root, var embeddedMod, var hasValidRoot) = await ArchiveModReader.LocateRoot(archive, readerProvider);
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
        var changelog = await builder.BuildAsync(ct);

        return new TkMod {
            Id = context.Id,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Changelog = changelog
        };
    }

    public bool IsKnownInput(object? input)
    {
        return input is string path
               && Path.GetExtension(path.AsSpan()) is ".7z";
    }
}