using System.Diagnostics.Contracts;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using RstbLibrary;
using RstbLibrary.Helpers;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Merging.ResourceSizeTable.Calculators;

namespace TkSharp.Merging.ResourceSizeTable;

// ReSharper disable StringLiteralTypo
public sealed class TkResourceSizeCollector
{
    private readonly ITkModWriter _writer;
    private readonly ITkRom _rom;
    private readonly string _relativePath;
    private readonly Rstb _result;
    private readonly Rstb _vanilla;

    public TkResourceSizeCollector(ITkModWriter writer, ITkRom rom)
    {
        _writer = writer;
        _rom = rom;

        _relativePath = _rom.GameVersion >= 140
            ? $"System/Resource/ResourceSizeTable.Product.{_rom.GameVersion}.Nin_NX_NVN.rsizetable.zs"
            : $"System/Resource/ResourceSizeTable.Product.{_rom.GameVersion}.rsizetable.zs";
        
        using var vanillaRstb = _rom.GetVanilla(_relativePath);
        _result = Rstb.FromBinary(vanillaRstb.Span);
        _vanilla = Rstb.FromBinary(vanillaRstb.Span);
    }
    
    public void Write()
    {
        using MemoryStream ms = new();
        _result.WriteBinary(ms);

        var buffer = ms.GetSpan();
        
        using var compressed = SpanOwner<byte>.Allocate(buffer.Count);
        var compressedData = compressed.Span;
        var compressedSize = _rom.Zstd.Compress(buffer, compressedData);

        using var output = _writer.OpenWrite(Path.Combine("romfs", _relativePath));
        output.Write(compressedData[..compressedSize]);
    }

    public void Collect(int fileSize, string path, in Span<byte> data)
    {
        var canonical = GetResourceName(path);
        var extension = GetResourceExtension(path);
        
        if (canonical is "Pack/ZsDic.pack" || extension is ".rsizetable" or ".bwav" or ".webm") {
            return;
        }

        fileSize += fileSize.AlignUp(0x20);
        
        var size = GetResourceSize(
            (uint)fileSize,
            canonical,
            extension,
            data);

        if (_result.OverflowTable.ContainsKey(canonical)) {
            lock (_result) {
                _result.OverflowTable[canonical] = size;
            }

            return;
        }

        var hash = Crc32.Compute(canonical);
        lock (_result) {
            if (_result.HashTable.TryAdd(hash, size)) {
                return;
            }
        }
        
        // If the hash is not in the vanilla
        // RSTB it is a hash collision
        if (!_vanilla.HashTable.ContainsKey(hash)) {
            lock (_result) {
                _result.OverflowTable[canonical] = size;
            }

            return;
        }

        lock (_result) {
            _result.HashTable[hash] = size;
        }
    }

    [Pure]
    public static bool RequiresDataForCalculation(ReadOnlySpan<char> path)
    {
        return GetResourceExtension(path) is ".ainb" or ".asb" or ".bstar" or ".mc";
    }

    [Pure]
    private static uint GetResourceSize(uint size, ReadOnlySpan<char> canonical, ReadOnlySpan<char> extension, Span<byte> data)
    {
        return canonical switch {
            "Event/EventFlow/Dm_ED_0004.bfevfl" => size + 0x1E0,
            "Effect/static.Nin_NX_NVN.esetb.byml" => size + 0x1000,
            "Effect/static.Product.110.Nin_NX_NVN.esetb.byml" => size + 0x1000,
            "Lib/agl/agl_resource.Nin_NX_NVN.release.sarc" => size + 0x1000,
            "Lib/gsys/gsys_resource.Nin_NX_NVN.release.sarc" => size + 0x1000,
            "Lib/Terrain/tera_resource.Nin_NX_NVN.release.sarc" => size + 0x1000,
            "Shader/ApplicationPackage.Nin_NX_NVN.release.sarc" => size + 0x1000,
            _ => extension switch {
                ".ainb" => size + AinbResourceSizeCalculator.GetResourceSize(data),
                ".asb" => size + AsbResourceSizeCalculator.GetResourceSize(data),
                ".bgyml" => (size + 2000) * 8,
                ".baatarc" => size + 0x100,
                ".baev" => size + 0x120,
                ".bagst" => size + 0x100,
                ".bars" => size + 0x240,
                ".bcul" => size + 0x100,
                ".beco" => size + 0x100,
                ".belnk" => size + 0x100,
                ".bfarc" => size + 0x100,
                ".bfevfl" => size + 0x120,
                ".bfsha" => size + 0x100,
                ".bhtmp" => size + 0x100,
                ".blal" => size + 0x100,
                ".blarc" => size + 0x100,
                ".blwp" => size + 0x100,
                ".bnsh" => size + 0x100,
                ".bntx" => size + 0x100,
                ".bphcl" => size + 0x100,
                ".bphhb" => size + 0x100,
                ".bphnm" => size + 0x120,
                ".bphsh" => size + 0x170,
                ".bslnk" => size + 0x100,
                ".bstar" => size + BstarResourceSizeCalculator.GetResourceSize(data),
                ".byml" => size + 0x100,
                ".cai" => size + 0x100,
                ".casset.byml" => size + 0x1C0,
                ".chunk" => size + 0x100,
                ".crbin" => size + 0x100,
                ".cutinfo" => size + 0x100,
                ".dpi" => size + 0x100,
                ".genvb" => size + 0x180,
                ".jpg" => size + 0x100,
                ".mc" => ModelCodecResourceCalculator.GetResourceSize(data),
                ".pack" => size + 0x180,
                ".png" => size + 0x100,
                ".quad" => size + 0x100,
                ".sarc" => size + 0x180,
                ".tscb" => size + 0x100,
                ".txtg" => size + 0x100,
                ".txt" => size + 0x100,
                ".vsts" => size + 0x100,
                ".wbr" => size + 0x100,
                ".zs" => size + 0x100,
                _ => (size + 1500) * 4
            }
        };
    }

    [Pure]
    private static ReadOnlySpan<char> GetResourceExtension(ReadOnlySpan<char> path)
    {
        return path.Length switch {
            > 15 when path[^15..] is ".casset.byml.zs" => ".casset.byml",
            > 6 when path[^6..] is ".ta.zs" => Path.GetExtension(path),
            > 3 when path[^3..] is ".zs" => Path.GetExtension(path[..^3]),
            _ => Path.GetExtension(path)
        };
    }

    [Pure]
    private static unsafe string GetResourceName(ReadOnlySpan<char> path)
    {
        var size = path.Length switch {
            > 3 when path[^3..] is ".zs" or ".mc" => path.Length - 3,
            _ => path.Length
        };
        
        var result = path[..size].ToString();
        Span<char> canonical;

        fixed (char* ptr = result) {
            canonical = new Span<char>(ptr, size);
        }

        for (var i = 0; i < size; i++) {
            ref var @char = ref canonical[i];
            @char = @char switch {
                '\\' => '/',
                _ => @char
            };
        }

        return result;
    }
}