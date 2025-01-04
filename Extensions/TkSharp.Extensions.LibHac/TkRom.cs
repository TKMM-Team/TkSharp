using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using TkSharp.Core;
using TkSharp.Core.IO;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.IO.Parsers;
using TkSharp.Extensions.LibHac.Extensions;

namespace TkSharp.Extensions.LibHac;

public class TkRom : ITkRom, IDisposable
{
    
    protected readonly TkChecksums _checksums;
    protected readonly IFileSystem _fileSystem;
    
    public int GameVersion { get; }

    public string NsoBinaryId { get; }

    public TkZstd Zstd { get; }

    public IDictionary<string, string> AddressTable { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EffectVersions { get; }

    public TkRom(TkChecksums checksums, IFileSystem fileSystem)
    {
        _checksums = checksums;
        _fileSystem = fileSystem;

        using (Stream regionLangMaskFs = _fileSystem.OpenFileStream("/System/RegionLangMask.txt"))
        using (RentedBuffer<byte> regionLangMask = RentedBuffer<byte>.Allocate(regionLangMaskFs)) {
            GameVersion = RegionLangMaskParser.ParseVersion(regionLangMask.Span, out string nsoBinaryId);
            NsoBinaryId = nsoBinaryId;
        }

        using (Stream zsDicFs = _fileSystem.OpenFileStream("/Pack/ZsDic.pack.zs")) {
            Zstd = new TkZstd(zsDicFs);
        }

        using (Stream addressTableFs = _fileSystem.OpenFileStream($"/System/AddressTable/Product.{GameVersion}.Nin_NX_NVN.atbl.byml.zs"))
        using (RentedBuffer<byte> addressTableBuffer = RentedBuffer<byte>.Allocate(addressTableFs)) {
            AddressTable = AddressTableParser.ParseAddressTable(addressTableBuffer.Span, Zstd);
        }

        using (Stream eventFlowFileEntryFs = _fileSystem.OpenFileStream($"/{AddressTable["Event/EventFlow/EventFlowFileEntry.Product.byml"]}.zs"))
        using (RentedBuffer<byte> eventFlowFileEntryBuffer = RentedBuffer<byte>.Allocate(eventFlowFileEntryFs)) {
            EventFlowVersions = EventFlowFileEntryParser.ParseFileEntry(eventFlowFileEntryBuffer.Span, Zstd);
        }

        using (Stream effectInfoFs = _fileSystem.OpenFileStream($"/{AddressTable["Effect/EffectFileInfo.Product.Nin_NX_NVN.byml"]}.zs"))
        using (RentedBuffer<byte> effectInfoBuffer = RentedBuffer<byte>.Allocate(effectInfoFs)) {
            EffectVersions = EffectInfoParser.ParseFileEntry(effectInfoBuffer.Span, Zstd);
        }
    }

    public RentedBuffer<byte> GetVanilla(string relativeFilePath)
    {
        relativeFilePath = $"/{relativeFilePath}";
        
        UniqueRef<IFile> file = new();
        _fileSystem.OpenFile(ref file, relativeFilePath.ToU8Span(), OpenMode.Read);

        if (!file.HasValue)
        {
            return default;
        }
        
        file.Get.GetSize(out long size);
        RentedBuffer<byte> rawBuffer = RentedBuffer<byte>.Allocate((int)size);
        Span<byte> raw = rawBuffer.Span;
        file.Get.Read(out _, offset: 0, raw);
        file.Destroy();
        
        if (!TkZstd.IsCompressed(raw))
        {
            return rawBuffer;
        }

        try
        {
            RentedBuffer<byte> decompressed = RentedBuffer<byte>.Allocate(TkZstd.GetDecompressedSize(raw)); 
            Zstd.Decompress(raw, decompressed.Span);
            return decompressed;
        }
        finally
        {
            rawBuffer.Dispose();
        }
    }

    public bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion)
    {
        return _checksums.IsFileVanilla(canonical, src, fileVersion);
    }

    public void Dispose()
    {
        _fileSystem.Dispose();
    }
} 