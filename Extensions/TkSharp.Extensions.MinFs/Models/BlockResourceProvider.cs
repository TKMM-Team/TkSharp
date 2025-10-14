using System.IO.Hashing;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Extensions.MinFs.Models;

public sealed class BlockResourceProvider
{
    private readonly Dictionary<ulong, BlockResourceEntry> _entries = [];

    public BlockResourceProvider(Stream metadataStream)
    {
        while (metadataStream.Position < metadataStream.Length) {
            var hash = metadataStream.Read<ulong>();
            _entries.Add(hash, new BlockResourceEntry(
                blockId: metadataStream.Read<ulong>(),
                offset: metadataStream.Read<long>(),
                size: metadataStream.Read<int>()
            ));
        }
    }
    
    public RentedBuffer<byte> this[string resourceName] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetResourceData(resourceName);
    }
    
    public RentedBuffer<byte> GetResourceData(string resourceName)
    {
        ulong resourceNameHash = XxHash64.HashToUInt64(resourceName.AsSpan().Cast<char, byte>());
        if (!_entries.TryGetValue(resourceNameHash, out var metadata)) {
            return default;
        }

        string blockFilePath = Path.Combine(AppContext.BaseDirectory, ".dump", metadata.BlockId.ToString());
        using var stream = File.OpenRead(blockFilePath);
        
        var result = RentedBuffer<byte>.Allocate(metadata.Size);
        
        stream.Seek(metadata.Offset, SeekOrigin.Begin);
        stream.ReadExactly(result.Span);
        
        return result;
    }
}