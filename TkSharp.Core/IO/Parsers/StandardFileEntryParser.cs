using BymlLibrary;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Core.IO.Parsers;

public static class StandardFileEntryParser
{
    public static Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> ParseStandardFileEntry(Span<byte> src, TkZstd zstd)
    {
        using RentedBuffer<byte> decompressed = RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(src));
        zstd.Decompress(src, decompressed.Span);
        
        return Byml.FromBinary(decompressed.Span)
            .GetMap()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetString())
            .GetAlternateLookup<ReadOnlySpan<char>>();
    }
}