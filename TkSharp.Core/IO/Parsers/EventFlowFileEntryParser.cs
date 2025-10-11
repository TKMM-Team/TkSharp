using BymlLibrary;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Core.IO.Parsers;

public static class EventFlowFileEntryParser
{
    public static Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> ParseFileEntry(Span<byte> src, TkZstd zstd)
    {
        using var decompressed = RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(src));
        zstd.Decompress(src, decompressed.Span);
        
        return Byml.FromBinary(decompressed.Span)
            .GetMap()["Versions"]
            .GetMap()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetString())
            .GetAlternateLookup<ReadOnlySpan<char>>();
    }
}