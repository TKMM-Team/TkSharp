using TkSharp.Core.Models;

namespace TkSharp.Core.Extensions;

public static class TkModReaderExtensions
{
    public static async Task<TkMod?> ReadFromInput(this ITkModReaderProvider readerProvider, object input, CancellationToken ct = default)
    {
        if (readerProvider.GetReader(input) is not { } reader) {
            return null;
        }

        if (input is string path && File.Exists(path)) {
            await using var fs = File.OpenRead(path);
            return await reader.ReadMod(input, fs, ct: ct)
                .ConfigureAwait(false);
        }

        return await reader.ReadMod(input, ct: ct)
            .ConfigureAwait(false);
    }
    
    public static async Task<TkMod?> ReadFromStream(this ITkModReaderProvider readerProvider, object input, Stream stream, CancellationToken ct = default)
    {
        if (readerProvider.GetReader(input) is not { } reader) {
            return null;
        }

        return await reader.ReadMod(input, stream, ct: ct)
            .ConfigureAwait(false);
    }
}