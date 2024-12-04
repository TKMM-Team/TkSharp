using TkSharp.Core.Exceptions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.IO.Parsers;

namespace TkSharp.Core.IO;

public sealed class ExtractedTkRom : ITkRom
{
    private readonly string _gamePath;
    private readonly TkChecksums _checksums;

    public ExtractedTkRom(string gamePath, TkChecksums checksums)
    {
        _gamePath = gamePath;
        _checksums = checksums;

        {
            string regionLangMaskPath = Path.Combine(gamePath, "System", "RegionLangMask.txt");
            if (!File.Exists(regionLangMaskPath)) {
                throw new GameRomException("RegionLangMask file not found.");
            }

            using Stream regionLangMaskFs = File.OpenRead(regionLangMaskPath);
            using RentedBuffer<byte> regionLangMask = RentedBuffer<byte>.Allocate(regionLangMaskFs);
            GameVersion = RegionLangMaskParser.ParseVersion(regionLangMask.Span, out string nsoBinaryId);
            NsoBinaryId = nsoBinaryId;
        }

        {
            string zsDicPath = Path.Combine(gamePath, "Pack", "ZsDic.pack.zs");
            if (!File.Exists(zsDicPath)) {
                throw new GameRomException("ZsDic file not found.");
            }
            
            using Stream zsDicFs = File.OpenRead(zsDicPath);
            Zstd = new TkZstd(zsDicFs);
        }

        {
            string addressTablePath = Path.Combine(gamePath, "System", "AddressTable", $"Product.{GameVersion}.Nin_NX_NVN.atbl.byml.zs");
            if (!File.Exists(addressTablePath)) {
                throw new GameRomException("System address table file not found.");
            }
            
            using Stream addressTableFs = File.OpenRead(addressTablePath);
            using RentedBuffer<byte> addressTableBuffer = RentedBuffer<byte>.Allocate(addressTableFs);
            AddressTable = AddressTableParser.ParseAddressTable(addressTableBuffer.Span, Zstd);
        }

        {
            string eventFlowFileEntryPath = Path.Combine(gamePath, $"{AddressTable["Event/EventFlow/EventFlowFileEntry.Product.byml"]}.zs");
            if (!File.Exists(eventFlowFileEntryPath)) {
                throw new GameRomException("Event flow file entry file not found.");
            }
            
            using Stream eventFlowFileEntryFs = File.OpenRead(eventFlowFileEntryPath);
            using RentedBuffer<byte> eventFlowFileEntryBuffer = RentedBuffer<byte>.Allocate(eventFlowFileEntryFs);
            EventFlowVersions = EventFlowFileEntryParser.ParseFileEntry(eventFlowFileEntryBuffer.Span, Zstd);
        }
    }
    
    public int GameVersion { get; }

    public string NsoBinaryId { get; }

    public TkZstd Zstd { get; }

    public IDictionary<string, string> AddressTable { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions { get; }

    public bool VanillaFileExists(string relativeFilePath)
    {
        string absolute = Path.Combine(_gamePath, relativeFilePath);
        return File.Exists(absolute);
    }

    public RentedBuffer<byte> GetVanilla(string relativeFilePath)
    {
        string absolute = Path.Combine(_gamePath, relativeFilePath);
        if (!File.Exists(absolute)) {
            return default;
        }
        
        using Stream fs = File.OpenRead(absolute);
        RentedBuffer<byte> raw = RentedBuffer<byte>.Allocate(fs);
        Span<byte> rawBuffer = raw.Span;

        if (!TkZstd.IsCompressed(rawBuffer)) {
            return raw;
        }

        try {
            RentedBuffer<byte> decompressed = RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(rawBuffer)); 
            Zstd.Decompress(rawBuffer, decompressed.Span);
            return decompressed;
        }
        finally {
            raw.Dispose();
        }
    }

    public bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion)
    {
        return _checksums.IsFileVanilla(canonical, src, fileVersion);
    }
}