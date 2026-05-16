using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using BfresLibrary;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace TkSharp.Merging.Mergers;

public sealed class BfresMcMerger(ITkRom rom) : ITkMerger
{
    private const string MATERIAL_PRODUCT_CANONICAL = "Shader/material.Product.product.Nin_NX_NVN.bfsha";
    private const string SHADER_STRING_CANONICAL = "Shader/ExternalBinaryString.bfres.mc";
    
    private const uint MCPK_MAGIC = 0x4B50434D;
    private const int MCPK_HEADER_SIZE = 0xC;

    private static readonly string[] ExpressionOptions =
    [
        "o_expression_pre_normal",
        "o_expression_post_normal"
    ];

    private static readonly Dictionary<string, List<Dictionary<int, long>>> MaterialDiff = LoadMaterialDiff();

    private static readonly Lock ProcessLock = new();

    private bool _stringCacheLoaded;

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
        => WriteOutput(output, ProcessBfresMc(input));

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
        => WriteOutput(output, ProcessBfresMc(inputs[^1].Segment));

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
        => WriteOutput(output, ProcessBfresMc(inputs.Last()));

    private static MergeResult WriteOutput(Stream output, byte[] data)
    {
        output.Write(data);
        return MergeResult.Default;
    }

    private void EnsureStringCacheLoaded()
    {
        if (_stringCacheLoaded) {
            return;
        }

        using var vanilla = rom.GetVanilla(SHADER_STRING_CANONICAL, out _);
        
        if (!vanilla.IsEmpty) {
            _ = new ResFile(new MemoryStream(DecompressMcpk(vanilla.Segment)));
        }

        _stringCacheLoaded = true;
    }

    private byte[] ProcessBfresMc(ArraySegment<byte> mcData)
    {
        lock (ProcessLock) {
            EnsureStringCacheLoaded();

            if (!TryOpenResFile(mcData, out var resFile)
                || !TryGetShaderProductVersion(out var userVersion)
                || !ApplyMaterialDiff(resFile, userVersion)) {
                return mcData.ToArray();
            }

            using var ms = new MemoryStream();
            resFile.Save(ms);
            return CompressMcpk(ms.ToArray());
        }
    }

    private static bool TryOpenResFile(ArraySegment<byte> mcData, out ResFile resFile)
    {
        try {
            resFile = new ResFile(new MemoryStream(DecompressMcpk(mcData)));
            return true;
        }
        catch {
            resFile = null!;
            return false;
        }
    }

    private static bool ApplyMaterialDiff(ResFile resFile, int userVersion)
    {
        var changed = false;

        foreach (var model in resFile.Models.Values) {
            if (model is null) {
                continue;
            }

            foreach (var mat in model.Materials.Values) {
                if (mat is null) {
                    continue;
                }

                foreach (var optionName in ExpressionOptions) {
                    if (!TryApplyOption(mat, model, optionName, userVersion)) {
                        continue;
                    }

                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool TryApplyOption(Material mat, Model model, string optionName, int userVersion)
    {
        if (FindOption(mat, optionName) is not { } option) {
            return false;
        }

        if (!TryParseOptionString(option.String, out var current)) {
            return false;
        }

        if (FindDiffRow(optionName, current) is not { } row) {
            return false;
        }

        var next = ValueForVersion(row, userVersion);
        if (next == current) {
            return false;
        }

        TkLog.Instance.LogInformation(
            "The material '{MaterialName}' in model '{ModelName}' requires an update, the BFRES file will be reserialized.",
            mat.Name,
            model.Name);

        option.String = next.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryGetShaderProductVersion(out int version)
    {
        if (!rom.AddressTable.TryGetValue(MATERIAL_PRODUCT_CANONICAL, out var resolved)) {
            version = 0;
            return false;
        }

        resolved.GetCanonical(out version, out _);
        return version >= 0;
    }

    private static ResString? FindOption(Material mat, string optionName)
    {
        var opts = mat.ShaderAssign?.ShaderOptions;

        if (opts is not { Count: > 0 }) {
            return null;
        }

        foreach (var kv in opts) {
            if (string.Equals(kv.Key, optionName, StringComparison.OrdinalIgnoreCase)) {
                return kv.Value;
            }
        }

        return null;
    }

    private static Dictionary<int, long>? FindDiffRow(string optionName, long value)
    {
        return !MaterialDiff.TryGetValue(optionName, out var rows)
            ? null
            : rows.FirstOrDefault(row => row.ContainsValue(value));
    }

    private static long ValueForVersion(Dictionary<int, long> row, int userVersion)
    {
        if (row.TryGetValue(userVersion, out var exact)) {
            return exact;
        }

        var bestAtOrBelow = int.MinValue;

        foreach (var k in row.Keys.Where(k => k <= userVersion && k > bestAtOrBelow)) {
            bestAtOrBelow = k;
        }

        return bestAtOrBelow != int.MinValue ? row[bestAtOrBelow] : row[row.Keys.Min()];
    }

    private static bool TryParseOptionString(string? s, out long value)
    {
        value = 0;
        s = s?.Trim();

        if (string.IsNullOrEmpty(s) || s.Equals("<Default Value>", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (s.Equals("True", StringComparison.OrdinalIgnoreCase)) {
            value = 1;
            return true;
        }

        if (s.Equals("False", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) {
            value = unchecked((long)hex);
            return true;
        }

        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) {
            return true;
        }

        if (!ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul) || ul > long.MaxValue) {
            return false;
        }

        value = (long)ul;
        return true;
    }

    private static Dictionary<string, List<Dictionary<int, long>>> LoadMaterialDiff()
    {
        using var stream = typeof(BfresMcMerger).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.MaterialDiff.json")!;
        using var doc = JsonDocument.Parse(stream);

        var diff = new Dictionary<string, List<Dictionary<int, long>>>(StringComparer.Ordinal);

        foreach (var prop in doc.RootElement.EnumerateObject()) {
            var rows = new List<Dictionary<int, long>>();

            foreach (var item in prop.Value.EnumerateArray()) {
                var row = new Dictionary<int, long>();

                foreach (var kv in item.EnumerateObject()) {
                    row[int.Parse(kv.Name, CultureInfo.InvariantCulture)] = kv.Value.GetInt64();
                }

                rows.Add(row);
            }

            diff[prop.Name] = rows;
        }

        return diff;
    }

    private static byte[] DecompressMcpk(ReadOnlySpan<byte> data)
    {
        if (data.Length < MCPK_HEADER_SIZE) {
            throw new InvalidDataException("MCPK file too small.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != MCPK_MAGIC) {
            throw new InvalidDataException("Not a MCPK file.");
        }

        var flags = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var decompSize = (uint)((flags >> 5) << (flags & 0xF));
        if (decompSize == 0) {
            throw new InvalidDataException("MCPK header declares zero decompressed size.");
        }

        var output = new byte[(int)decompSize];
        using var dec = new Decompressor();
        dec.SetParameter(ZSTD_dParameter.ZSTD_d_experimentalParam1, (int)ZSTD_format_e.ZSTD_f_zstd1_magicless);

        try {
            dec.Unwrap(data[MCPK_HEADER_SIZE..], output);
        }
        catch (ZstdException) { }

        return output;
    }

    private static byte[] CompressMcpk(byte[] src)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(MCPK_MAGIC);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(GetMcpkFlags((uint)src.Length));
        writer.Write(CompressMagiclessZstd(src));
        return ms.ToArray();
    }

    private static uint GetMcpkFlags(uint decompSize)
    {
        var aligned = (uint)(-decompSize % 0x1000 + 0x1000) % 0x1000;
        decompSize += aligned;
        return ((decompSize >> 0xC) << 5) + 0xC;
    }

    private static byte[] CompressMagiclessZstd(byte[] src)
    {
        using var comp = new Compressor(20);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_dictIDFlag, 0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_experimentalParam2, 1);
        return comp.Wrap(src).ToArray();
    }
}
