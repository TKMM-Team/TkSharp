using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.IO.Caching;
using TkSharp.Core.IO.Parsers;
using TkSharp.Extensions.MinFs.Models;

namespace TkSharp.Extensions.MinFs.IO;

/// <summary>
/// ITkRom implementation for a block-compiled minimized file system ROM.
/// </summary>
public sealed class MinFsRom : ITkRom
{
    private readonly TkChecksums _checksums;
    private readonly TkPackFileLookup _packFileLookup;
    private readonly BlockResourceProvider _blockResourceProvider;

    public int GameVersion { get; }

    public string NsoBinaryId { get; }

    public TkZstd Zstd { get; }

    public IDictionary<string, string> AddressTable { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EffectVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> AiVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> LogicVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> SequenceVersions { get; }

    public MinFsRom(TkChecksums checksums, TkPackFileLookup packFileLookup, BlockResourceProvider blockResourceProvider)
    {
        _checksums = checksums;
        _packFileLookup = packFileLookup;
        _blockResourceProvider = blockResourceProvider;
        
        using (var regionLangMask = blockResourceProvider["/System/RegionLangMask.txt"]) {
            GameVersion = RegionLangMaskParser.ParseVersion(regionLangMask.Span, out string nsoBinaryId);
            NsoBinaryId = nsoBinaryId;
        }

        using (var zsDicFs = blockResourceProvider["/Pack/ZsDic.pack.zs"]) {
            Zstd = new TkZstd(zsDicFs.Span);
        }

        using (var addressTableBuffer = blockResourceProvider[$"/System/AddressTable/Product.{GameVersion}.Nin_NX_NVN.atbl.byml.zs"]) {
            AddressTable = AddressTableParser.ParseAddressTable(addressTableBuffer.Span, Zstd);
        }

        using (var eventFlowFileEntryBuffer = blockResourceProvider[$"/{AddressTable["Event/EventFlow/EventFlowFileEntry.Product.byml"]}.zs"]) {
            EventFlowVersions = EventFlowFileEntryParser.ParseFileEntry(eventFlowFileEntryBuffer.Span, Zstd);
        }

        using (var effectInfoBuffer = blockResourceProvider[$"/{AddressTable["Effect/EffectFileInfo.Product.Nin_NX_NVN.byml"]}.zs"]) {
            EffectVersions = EffectInfoParser.ParseFileEntry(effectInfoBuffer.Span, Zstd);
        }

        using (var ainbFileEntryBuffer = blockResourceProvider[$"/{AddressTable["AI/FileEntry/FileEntry.Product.byml"]}.zs"]) {
            AiVersions = StandardFileEntryParser.ParseStandardFileEntry(ainbFileEntryBuffer.Span, Zstd);
        }

        using (var logicFileEntryBuffer = blockResourceProvider[$"/{AddressTable["Logic/FileEntry/FileEntry.Product.byml"]}.zs"]) {
            LogicVersions = StandardFileEntryParser.ParseStandardFileEntry(logicFileEntryBuffer.Span, Zstd);
        }

        using (var sequenceFileEntryBuffer = blockResourceProvider[$"/{AddressTable["Sequence/FileEntry/FileEntry.Product.byml"]}.zs"]) {
            SequenceVersions = StandardFileEntryParser.ParseStandardFileEntry(sequenceFileEntryBuffer.Span, Zstd);
        }
    }

    public RentedBuffer<byte> GetVanilla(string relativeFilePath, out bool isFoundMissing)
    {
        var result = _blockResourceProvider[relativeFilePath];
        if (result.IsEmpty) {
            result.Dispose();
            return _packFileLookup.GetNested(relativeFilePath, this, out isFoundMissing);
        }
        
        isFoundMissing = false;
        ReadOnlySpan<byte> raw = result.Span;
        
        if (!TkZstd.IsCompressed(raw)) {
            return result;
        }

        try {
            var decompressed = RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(raw));
            Zstd.Decompress(raw, decompressed.Span);
            return decompressed;
        }
        finally {
            result.Dispose();
        }
    }

    public bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion)
    {
        return _checksums.IsFileVanilla(canonical, src, fileVersion);
    }
    
    
    public void Dispose()
    {
        _packFileLookup.Dispose();
    }
}