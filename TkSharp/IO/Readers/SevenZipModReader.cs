using SharpCompress.Archives.SevenZip;
using TkSharp.Core;
using TkSharp.Core.Models;

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
        
        using var archive = SevenZipArchive.OpenArchive(context.Stream);
        return await ArchiveModReader.ReadArchiveMod(archive, fileName, context, _systemProvider, _romProvider, readerProvider, ct)
            .ConfigureAwait(false);
    }

    public bool IsKnownInput(object? input)
    {
        return input is string path
               && Path.GetExtension(path.AsSpan()) is ".7z";
    }
}