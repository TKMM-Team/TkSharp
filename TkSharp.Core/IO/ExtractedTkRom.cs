using TkSharp.Core.Exceptions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.IO.Caching;
using TkSharp.Core.IO.Parsers;

namespace TkSharp.Core.IO;

public sealed class ExtractedTkRom : ITkRom
{
    private readonly string _gamePath;
    private readonly TkChecksums _checksums;
    private readonly TkPackFileLookup _packFileLookup;

    public ExtractedTkRom(string gamePath, TkChecksums checksums, TkPackFileLookup packFileLookup)
    {
        _gamePath = gamePath;
        _checksums = checksums;
        _packFileLookup = packFileLookup;

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

        {
            string effectInfoPath = Path.Combine(gamePath, $"{AddressTable["Effect/EffectFileInfo.Product.Nin_NX_NVN.byml"]}.zs");
            if (!File.Exists(effectInfoPath)) {
                throw new GameRomException("Effect info file entry file not found.");
            }
            
            using Stream effectInfoFs = File.OpenRead(effectInfoPath);
            using RentedBuffer<byte> effectInfoBuffer = RentedBuffer<byte>.Allocate(effectInfoFs);
            EffectVersions = EffectInfoParser.ParseFileEntry(effectInfoBuffer.Span, Zstd);
        }

        {
            string ainbFileEntryPath = Path.Combine(gamePath, $"{AddressTable["AI/FileEntry/FileEntry.Product.byml"]}.zs");
            if (!File.Exists(ainbFileEntryPath)) {
                throw new GameRomException("AI file entry file not found.");
            }
            
            using Stream ainbFileEntryFs = File.OpenRead(ainbFileEntryPath);
            using RentedBuffer<byte> ainbFileEntryBuffer = RentedBuffer<byte>.Allocate(ainbFileEntryFs);
            AiVersions = StandardFileEntryParser.ParseStandardFileEntry(ainbFileEntryBuffer.Span, Zstd);
        }

        {
            string logicFileEntryPath = Path.Combine(gamePath, $"{AddressTable["Logic/FileEntry/FileEntry.Product.byml"]}.zs");
            if (!File.Exists(logicFileEntryPath)) {
                throw new GameRomException("Logic file entry file not found.");
            }
            
            using Stream logicFileEntryFs = File.OpenRead(logicFileEntryPath);
            using RentedBuffer<byte> logicFileEntryBuffer = RentedBuffer<byte>.Allocate(logicFileEntryFs);
            LogicVersions = StandardFileEntryParser.ParseStandardFileEntry(logicFileEntryBuffer.Span, Zstd);
        }

        {
            string sequenceFileEntryPath = Path.Combine(gamePath, $"{AddressTable["Sequence/FileEntry/FileEntry.Product.byml"]}.zs");
            if (!File.Exists(sequenceFileEntryPath)) {
                throw new GameRomException("Sequence file entry file not found.");
            }
            
            using Stream sequenceFileEntryFs = File.OpenRead(sequenceFileEntryPath);
            using RentedBuffer<byte> sequenceFileEntryBuffer = RentedBuffer<byte>.Allocate(sequenceFileEntryFs);
            SequenceVersions = StandardFileEntryParser.ParseStandardFileEntry(sequenceFileEntryBuffer.Span, Zstd);
        }
    }
    
    public int GameVersion { get; }

    public string NsoBinaryId { get; }

    public TkZstd Zstd { get; }

    public IDictionary<string, string> AddressTable { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EffectVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> AiVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> LogicVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> SequenceVersions { get; }

    public RentedBuffer<byte> GetVanilla(string relativeFilePath, out bool isFoundMissing)
    {
        isFoundMissing = false;
        string absolute = Path.Combine(_gamePath, relativeFilePath);
        if (!File.Exists(absolute)) {
            // Nested files have the relative and canonical
            // file paths, so this is a valid call
            return _packFileLookup.GetNested(relativeFilePath, this, out isFoundMissing);
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

    public void Dispose()
    {
        Zstd.Dispose();
    }
}