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
    
    private ITkRom? _rom;
    private Stream? _precompiled = precompiled;
    private readonly FrozenDictionary<ulong, string> _lookup = CreateLookup(pkcache);

    public TkPackFileLookup Init(ITkRom rom)
    {
        _rom = rom;
        return this;
    }

    public TkPackFileLookup UsePrecompiledCache(Stream precompiled)
    {
        _precompiled = precompiled;
        return this;
    }
    
    /// <summary>
    /// Returns the pack file data or an empty buffer if the file does not exist
    /// </summary>
    /// <param name="canonical"></param>
    /// <returns></returns>
    public RentedBuffer<byte> GetNested(string canonical)
    {
        if (_rom is null) {
            throw new InvalidOperationException("Pack file lookup is not initialized.");
        }
        
        if (GetPackFileName(canonical, out RentedBuffer<byte> buffer) is string packFileName) {
            RentedBuffer<byte> sarcBuffer = _rom.GetVanilla(packFileName);
            RevrsReader reader = new(sarcBuffer.Span);
            ImmutableSarc sarc = new(ref reader);
            ImmutableSarcEntry entry = sarc[canonical];
            sarcBuffer.Slice(entry.DataStartOffset, entry.DataEndOffset);
            return sarcBuffer;
        }

        return buffer;
    }

    /// <summary>
    /// Returns the pack file name or the vanilla data if the entries are precompiled
    /// </summary>
    /// <param name="canonical"></param>
    /// <param name="buffer"></param>
    /// <returns></returns>
    private string? GetPackFileName(string canonical, out RentedBuffer<byte> buffer)
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
    
    private static unsafe FrozenDictionary<ulong, string> CreateLookup(Stream pkcache)
    {
        const uint magic = 0x48434B50;
        
        using RentedBuffer<byte> compressed = RentedBuffer<byte>.Allocate(pkcache);
        using RentedBuffer<byte> buffer = RentedBuffer<byte>.Allocate(
            TkZstd.GetDecompressedSize(compressed.Span)
        );
        
        _zstd.Unwrap(compressed.Span, buffer.Span);
        
        RevrsReader reader = RevrsReader.Native(buffer.Span);
        if (reader.Read<uint>() != magic) {
            throw new InvalidDataException($"Invalid pkcache magic");
        }

        int count = reader.Read<int>();
        int stringTableOffset = reader.Read<int>();
        int stringCount = reader.Read<int>();
        
        using SpanOwner<string> valuesOwner = SpanOwner<string>.Allocate(stringCount);
        Span<string> values = valuesOwner.Span;

        reader.Seek(stringTableOffset);
        fixed (byte* ptr = &reader.Data[reader.Position]) {
            byte* pos = ptr;
            for (int i = 0; i < stringCount; i++) {
                string str = values[i] = Utf8StringMarshaller.ConvertToManaged(pos)
                    ?? throw new InvalidDataException($"Invalid string in string table at: {i}");
                pos += str.Length + 1;
            }
        }
        
        reader.Seek(0x10);

        Dictionary<ulong, string> result = [];

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