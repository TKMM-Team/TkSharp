using System.Collections.Frozen;
using System.IO.Hashing;
using System.Runtime.InteropServices.Marshalling;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using SarcLibrary;
using TkSharp.Core.IO.Buffers;
using ZstdSharp;

namespace TkSharp.Core.IO.Caching;

public sealed class TkPackFileLookup(Stream pkcache, Stream? precompiled = null) : IDisposable
{
    private static readonly Decompressor _zstd = new();
    
    private Stream? _precompiled = precompiled;
    private readonly FrozenDictionary<ulong, (string, TkFileAttributes)> _lookup = CreateLookup(pkcache);

    public TkPackFileLookup UsePrecompiledCache(Stream precompiled)
    {
        _precompiled = precompiled;
        return this;
    }

    /// <summary>
    /// Returns the pack file data or an empty buffer if the file does not exist
    /// </summary>
    /// <param name="canonical"></param>
    /// <param name="rom"></param>
    /// <param name="isFoundMissing">true if the entry was recorded to exist, but cannot be found in the game dump</param>
    /// <returns></returns>
    public RentedBuffer<byte> GetNested(string canonical, ITkRom rom, out bool isFoundMissing)
    {
        isFoundMissing = false;
        
        if (GetPackFileName(canonical, out var buffer) is (string packFileCanonical, var attributes)) {
            var sarcBuffer = rom.GetVanilla(packFileCanonical, attributes);
            RevrsReader reader = new(sarcBuffer.Span);
            ImmutableSarc sarc = new(ref reader);
            if (sarc.TryGet(canonical, out var entry)) {
                sarcBuffer.Slice(entry.DataStartOffset, entry.DataEndOffset);
                return sarcBuffer;
            }

            sarcBuffer.Dispose();
            isFoundMissing = true;
            return default;
        }

        return buffer;
    }

    /// <summary>
    /// Returns the pack file name or the vanilla data if the entries are precompiled
    /// </summary>
    /// <param name="canonical"></param>
    /// <param name="buffer"></param>
    /// <returns></returns>
    private (string, TkFileAttributes)? GetPackFileName(string canonical, out RentedBuffer<byte> buffer)
    {
        if (_precompiled != null) {
            SarcTools.JumpToEntry(_precompiled, canonical, out int size);
            buffer = RentedBuffer<byte>.Allocate(size);
            _precompiled.ReadExactly(buffer.Span);
            return null;
        }

        buffer = default;
        return _lookup.GetValueOrDefault(GetKey(canonical));
    }
    
    private static unsafe FrozenDictionary<ulong, (string, TkFileAttributes)> CreateLookup(Stream pkcache)
    {
        const uint magic = 0x48434B50;
        
        using var compressed = RentedBuffer<byte>.Allocate(pkcache);
        using var buffer = RentedBuffer<byte>.Allocate(
            TkZstd.GetDecompressedSize(compressed.Span)
        );
        
        _zstd.Unwrap(compressed.Span, buffer.Span);
        
        var reader = RevrsReader.Native(buffer.Span);
        if (reader.Read<uint>() != magic) {
            throw new InvalidDataException("Invalid pkcache magic");
        }

        int count = reader.Read<int>();
        int stringTableOffset = reader.Read<int>();
        int stringCount = reader.Read<int>();
        
        using var valuesOwner = SpanOwner<(string, TkFileAttributes)>.Allocate(stringCount);
        var values = valuesOwner.Span;

        reader.Seek(stringTableOffset);
        fixed (byte* ptr = &reader.Data[reader.Position]) {
            byte* pos = ptr;
            for (int i = 0; i < stringCount; i++, pos++) {
                string str = Utf8StringMarshaller.ConvertToManaged(pos)
                    ?? throw new InvalidDataException($"Invalid string in string table at: {i}");
                pos += str.Length + 1;
                values[i] = (str, (TkFileAttributes)(*pos));
            }
        }
        
        reader.Seek(0x10);

        Dictionary<ulong, (string, TkFileAttributes)> result = [];

        for (int i = 0; i < count; i++) {
            ushort sectionKey = reader.Read<ushort>();
            uint hash = reader.Read<uint>();
            ushort index = reader.Read<ushort>();
            result.Add(GetKey(sectionKey, hash), values[index]);
        }

        return result.ToFrozenDictionary();
    }

    private static ulong GetKey(ReadOnlySpan<char> key)
    {
        ushort sectionKey = (ushort)((byte)key[0] << 8 | (byte)key[^1]);
        uint hash = XxHash32.HashToUInt32(key.Cast<char, byte>());
        return GetKey(sectionKey, hash);
    }

    private static ulong GetKey(ushort sectionKey, uint hash)
    {
        return (ulong)hash << 32 | sectionKey;
    }

    public void Dispose()
    {
        _precompiled?.Dispose();
    }
}