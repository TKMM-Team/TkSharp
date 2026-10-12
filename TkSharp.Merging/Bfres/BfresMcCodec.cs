using System.Buffers.Binary;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace TkSharp.Merging.Bfres;

internal static class BfresMcCodec
{
    private const uint MCPK_MAGIC = 0x4B50434D;
    private const int MCPK_HEADER_SIZE = 0xC;

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < MCPK_HEADER_SIZE) {
            throw new InvalidDataException("MCPK file too small.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != MCPK_MAGIC) {
            throw new InvalidDataException("Not a MCPK file.");
        }

        var flags = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var decompSize = (uint)((flags >> 5) << (flags & 0xF));
        if (decompSize == 0) {
            throw new InvalidDataException("MCPK header declares zero decompressed size.");
        }

        var output = new byte[(int)decompSize];
        using var dec = new Decompressor();
        dec.SetParameter(ZSTD_dParameter.ZSTD_d_experimentalParam1, (int)ZSTD_format_e.ZSTD_f_zstd1_magicless);

        try {
            dec.Unwrap(data[MCPK_HEADER_SIZE..], output);
        }
        catch (ZstdException) { }

        return output;
    }

    public static byte[] Compress(byte[] src)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(MCPK_MAGIC);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(GetFlags((uint)src.Length));
        writer.Write(CompressMagiclessZstd(src));
        return ms.ToArray();
    }

    private static uint GetFlags(uint decompSize)
    {
        var aligned = (uint)(-decompSize % 0x1000 + 0x1000) % 0x1000;
        decompSize += aligned;
        return ((decompSize >> 0xC) << 5) + 0xC;
    }

    private static byte[] CompressMagiclessZstd(byte[] src)
    {
        using var comp = new Compressor(20);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_dictIDFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_experimentalParam2, 1);
        return [.. comp.Wrap(src)];
    }
}
