using Revrs.Extensions;
using SharpCompress.Common.Zip;
using SharpCompress.Readers.Zip;
using TkSharp.Core;
using TkSharp.Core.IO.Serialization;
using TkSharp.Core.Models;
using static TkSharp.Core.IO.Serialization.TkBinaryWriter;

namespace TkSharp.IO.Readers;

public sealed class TkPackReader(ITkSystemProvider systemProvider) : ITkModReader
{
    private readonly ITkSystemProvider _systemProvider = systemProvider;
    
    public ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        return ReadMod(context, context.Stream, ct);
    }

    public async ValueTask<TkMod?> ReadMod(TkModContext context, Stream? stream = null, CancellationToken ct = default)
    {
        if (stream is null) {
            throw new ArgumentException(
                "Input stream must not be null.", nameof(stream)
            );
        }

        if (stream.Read<uint>() != TKPK_MAGIC) {
            throw new InvalidDataException(
                "Invalid TotK mod pack magic.");
        }

        if (stream.Read<uint>() != TKPK_VERSION) {
            throw new InvalidDataException(
                "Unexpected TotK mod pack version. Expected 2.0.0");
        }

        var result = TkBinaryReader.ReadTkMod(context, stream, _systemProvider);

        var reader = ZipReader.Open(stream);
        
        var writer = _systemProvider.GetSystemWriter(context);
        while (reader.MoveToNextEntry()) {
            var entry = reader.Entry;
            await using Stream archiveStream = reader.OpenEntryStream();
            await using var output = writer.OpenWrite(entry.Key!.Replace('\\', '/'));
            await archiveStream.CopyToAsync(output, ct);
        }

        return result;
    }

    public bool IsKnownInput(object? input)
    {
        return input is string path &&
               Path.GetExtension(path.AsSpan()) is ".tkcl";
    }
}