using System.Collections.Frozen;
using CommunityToolkit.HighPerformance;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using ZstdSharp;

namespace TkSharp.Merging.ChangelogBuilders.GameData;

public static class GameDataCache
{
    private const uint MAGIC = 0x434C4447; // GDLC

    private static readonly Decompressor Zstd = new();
    private static readonly FrozenDictionary<int, byte[]> VersionsDictionary;

    public static ReadOnlySpan<int> Versions => VersionList;

    private static readonly int[] VersionList;

    static GameDataCache()
    {
        using var compressedStream = typeof(GameDataCache).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.GameDataCache.gdlc.zs")!;

        using var compressed = RentedBuffer<byte>.Allocate(compressedStream);
        var decompressedBytes = new byte[TkZstd.GetDecompressedSize(compressed.Span)];
        Zstd.Unwrap(compressed.Span, decompressedBytes);

        using MemoryStream stream = new(decompressedBytes);
        if (stream.Read<uint>() != MAGIC) {
            throw new InvalidDataException("Invalid GameDataCache magic");
        }

        var versionCount = stream.Read<int>();
        Dictionary<int, byte[]> versions = new(versionCount);

        for (var i = 0; i < versionCount; i++) {
            var version = stream.Read<int>();
            var dataSize = stream.Read<int>();
            var data = new byte[dataSize];
            _ = stream.Read(data);
            versions.Add(version, data);
        }

        VersionList = versions.Keys.Order().ToArray();
        VersionsDictionary = versions.ToFrozenDictionary();
    }

    public static bool TryGet(int version, out byte[] data)
        => VersionsDictionary.TryGetValue(version, out data!);
}
