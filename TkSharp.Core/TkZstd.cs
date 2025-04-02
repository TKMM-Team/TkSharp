using System.Buffers;
using System.Diagnostics;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using Revrs.Extensions;
using SarcLibrary;
using TkSharp.Core.IO.Buffers;
using ZstdSharp;

namespace TkSharp.Core;

public class TkZstd
{
    public const uint ZSTD_MAGIC = 0xFD2FB528;
    private const uint DICT_MAGIC = 0xEC30A437;
    private const uint SARC_MAGIC = 0x43524153;

    private readonly Decompressor _defaultDecompressor = new();
    private readonly Dictionary<int, Decompressor> _decompressors = [];
    private readonly Compressor _defaultCompressor;
    private readonly Dictionary<int, Compressor> _compressors = [];
    private int _compressionLevel = 7;

    public TkZstd(in Stream zsDicPack)
    {
        _defaultCompressor = new Compressor(CompressionLevel);
        LoadDictionaries(zsDicPack);
    }

    public int CompressionLevel {
        get => _compressionLevel;
        set {
            _compressionLevel = value;
            _defaultCompressor.Level = _compressionLevel;

            foreach ((int _, Compressor compressor) in _compressors) {
                compressor.Level = value;
            }
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> data) => Decompress(data, out _);

    public void Decompress(ReadOnlySpan<byte> data, Span<byte> dst) => Decompress(data, dst, out _);

    public RentedBuffer<byte> Decompress(in Stream stream) => Decompress(stream, out _);
    
    public RentedBuffer<byte> Decompress(in Stream stream, out int zsDictionaryId)
    {
        RentedBuffer<byte> src = RentedBuffer<byte>.Allocate(stream);
        Span<byte> srcSpan = src.Span;
        if (srcSpan.Length < 4 || srcSpan.Read<uint>() != ZSTD_MAGIC) {
            zsDictionaryId = -1;
            return src;
        }

        try {
            RentedBuffer<byte> dst = RentedBuffer<byte>.Allocate(
                GetDecompressedSize(src.Span));
            Decompress(src.Span, dst.Span, out zsDictionaryId);
            return dst;
        }
        finally {
            src.Dispose();
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> data, out int zsDictionaryId)
    {
        int size = GetDecompressedSize(data);
        byte[] result = new byte[size];
        Decompress(data, result, out zsDictionaryId);
        return result;
    }

    public void Decompress(ReadOnlySpan<byte> data, Span<byte> dst, out int zsDictionaryId)
    {
        if (!IsCompressed(data)) {
            zsDictionaryId = -1;
            return;
        }

        zsDictionaryId = GetDictionaryId(data);
        lock (_decompressors) {
            if (_decompressors.TryGetValue(zsDictionaryId, out Decompressor? decompressor)) {
                decompressor.Unwrap(data, dst);
                return;
            }
        }

        lock (_defaultDecompressor) {
            _defaultDecompressor.Unwrap(data, dst);
        }
    }

    public RentedBuffer<byte> Compress(ReadOnlySpan<byte> data, int zsDictionaryId = -1)
    {
        int bounds = Compressor.GetCompressBound(data.Length);
        RentedBuffer<byte> result = RentedBuffer<byte>.Allocate(bounds);
        int size = Compress(data, result.Span, zsDictionaryId);
        result.Resize(size);
        return result;
    }

    public int Compress(ReadOnlySpan<byte> data, Span<byte> dst, int zsDictionaryId = -1)
    {
        lock (_compressors) {
            return _compressors.TryGetValue(zsDictionaryId, out Compressor? compressor) switch {
                true => compressor.Wrap(data, dst),
                false => _defaultCompressor.Wrap(data, dst)
            };
        }
    }

    public static bool IsCompressed(ReadOnlySpan<byte> data)
    {
        return data.Length > 3 &&
               data.Read<uint>() == ZSTD_MAGIC;
    }

    public static bool IsCompressed(in Stream stream)
    {
        if (stream.Length < sizeof(uint)) {
            return false;
        }
        
        bool result = stream.Read<uint>() == ZSTD_MAGIC;
        stream.Seek(-sizeof(uint), SeekOrigin.Current);
        return result;
    }

    public static int GetDecompressedSize(in Stream stream)
    {
        Span<byte> header = stackalloc byte[14];
        _ = stream.Read(header);
        stream.Seek(-14, SeekOrigin.Current);
        return GetFrameContentSize(header);
    }

    public static int GetDecompressedSize(ReadOnlySpan<byte> data)
    {
        return GetFrameContentSize(data);
    }

    public static int GetDictionaryId(ReadOnlySpan<byte> buffer)
    {
        byte descriptor = buffer[4];
        int windowDescriptorSize = ((descriptor & 0b00100000) >> 5) ^ 0b1;
        int dictionaryIdFlag = descriptor & 0b00000011;

        return dictionaryIdFlag switch {
            0x0 => -1,
            0x1 => buffer[5 + windowDescriptorSize],
            0x2 => buffer[(5 + windowDescriptorSize)..].Read<ushort>(),
            0x3 => buffer[(5 + windowDescriptorSize)..].Read<int>(),
            _ => throw new OverflowException(
                "Two bits cannot exceed 0x3, something terrible has happened!")
        };
    }

    private static int GetFrameContentSize(ReadOnlySpan<byte> buffer)
    {
        byte descriptor = buffer[4];
        int windowDescriptorSize = ((descriptor & 0b00100000) >> 5) ^ 0b1;
        int dictionaryIdFlag = descriptor & 0b00000011;
        int frameContentFlag = descriptor >> 6;

        int offset = dictionaryIdFlag switch {
            0x0 => 5 + windowDescriptorSize,
            0x1 => 5 + windowDescriptorSize + 1,
            0x2 => 5 + windowDescriptorSize + 2,
            0x3 => 5 + windowDescriptorSize + 4,
            _ => throw new OverflowException(
                "Two bits cannot exceed 0x3, something terrible has happened!")
        };

        return frameContentFlag switch {
            0x0 => buffer[offset],
            0x1 => buffer[offset..].Read<ushort>() + 0x100,
            0x2 => buffer[offset..].Read<int>(),
            _ => throw new NotSupportedException(
                "64-bit file sizes are not supported.")
        };
    }

    public void LoadDictionaries(in Stream stream)
    {
        int size = Convert.ToInt32(stream.Length);
        using SpanOwner<byte> buffer = SpanOwner<byte>.Allocate(size);
        int read = stream.Read(buffer.Span);
        Debug.Assert(size == read);
        LoadDictionaries(buffer.Span);
    }

    public void LoadDictionaries(Span<byte> data)
    {
        byte[]? decompressedBuffer = null;

        if (IsCompressed(data)) {
            int decompressedSize = GetDecompressedSize(data);
            decompressedBuffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            Span<byte> decompressed = decompressedBuffer.AsSpan()[..decompressedSize];
            Decompress(data, decompressed, out _);
            data = decompressed;
        }

        if (TryLoadDictionary(data)) {
            return;
        }

        if (data.Length < 8 || data.Read<uint>() != SARC_MAGIC) {
            return;
        }

        RevrsReader reader = new(data);
        ImmutableSarc sarc = new(ref reader);
        foreach ((string _, Span<byte> sarcFileData) in sarc) {
            TryLoadDictionary(sarcFileData);
        }

        if (decompressedBuffer is not null) {
            ArrayPool<byte>.Shared.Return(decompressedBuffer);
        }
    }

    private bool TryLoadDictionary(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8 || buffer.Read<uint>() != DICT_MAGIC) {
            return false;
        }

        Decompressor decompressor = new();
        decompressor.LoadDictionary(buffer);
        lock (_decompressors) {
            _decompressors[buffer[4..8].Read<int>()] = decompressor;
        }

        Compressor compressor = new(CompressionLevel);
        compressor.LoadDictionary(buffer);
        _compressors[buffer[4..8].Read<int>()] = compressor;

        return true;
    }
}