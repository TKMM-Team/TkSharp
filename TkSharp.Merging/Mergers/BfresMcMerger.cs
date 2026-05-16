using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using BfresLibrary;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace TkSharp.Merging.Mergers;

public sealed class BfresMcMerger(ITkRom rom) : ITkMerger
{
    private const string OPTION_PRE_NORMAL  = "o_expression_pre_normal";
    private const string OPTION_POST_NORMAL = "o_expression_post_normal";
    private static readonly string[] Options = [OPTION_PRE_NORMAL, OPTION_POST_NORMAL];
    private const uint MC_PK_MAGIC_LE = 0x4B50434D;
    private const int HEADER_SIZE = 0xC;

    private static readonly Dictionary<string, List<Dictionary<int, long>>> MaterialDiff;
    private static readonly int[] MaterialShaderVersions;
    private static readonly Lock SProcessLock = new();

    private bool _stringCacheLoaded;

    static BfresMcMerger() => MaterialDiff = LoadMaterialDiff(out MaterialShaderVersions);

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        output.Write(ProcessBfresMc(input));
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        output.Write(ProcessBfresMc(inputs[^1].Segment));
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        output.Write(ProcessBfresMc(inputs.Last()));
        return MergeResult.Default;
    }

    private void LoadStringCache()
    {
        if (_stringCacheLoaded) {
            return;
        }
        
        const string relativePath = "Shader/ExternalBinaryString.bfres.mc";
        using var vanilla = rom.GetVanilla(relativePath, out _);
        _ = new ResFile(new MemoryStream(DecompressMcpk(vanilla.Segment)));
        _stringCacheLoaded = true;
    }

    private byte[] ProcessBfresMc(ArraySegment<byte> mcData)
    {
        lock (SProcessLock) {
            LoadStringCache();

            byte[] bfresData;

            try {
                bfresData = DecompressMcpk(mcData);
            }
            catch {
                return mcData.ToArray();
            }

            ResFile resFile;

            try {
                resFile = new ResFile(new MemoryStream(bfresData));
            }
            catch {
                return mcData.ToArray();
            }

            if (!TryGetShaderProductVersion(out var userVersion))
                return mcData.ToArray();

            var anyApplied = false;
            
            foreach (var model in resFile.Models.Values) {
                if (model is null) {
                    continue;
                }
                
                foreach (var mat in model.Materials.Values) {
                    if (mat is null) {
                        continue;
                    }
                    
                    foreach (var opt in Options) {
                        if (!TryGetOptionValue(mat, opt, out var cur)) {
                            continue;
                        }
                        
                        var row = FindDiffRow(opt, cur);
                        
                        if (row is null) {
                            continue;
                        }
                        
                        var next = ValueForVersion(row, userVersion);
                        
                        if (next == cur) {
                            continue;
                        }
                        
                        SetOptionValue(mat, opt, next);
                        anyApplied = true;
                    }
                }
            }

            if (!anyApplied) {
                return mcData.ToArray();
            }

            using var ms = new MemoryStream();
            resFile.Save(ms);
            return CompressMcpk(ms.ToArray());
        }
    }

    private bool TryGetShaderProductVersion(out int version)
    {
        foreach (var v in MaterialShaderVersions) {
            var name = v.ToString(CultureInfo.InvariantCulture);
            var relativePath = $"Shader/material.Product.{name}.product.Nin_NX_NVN.bfsha.zs";
            using var probe = rom.GetVanilla(relativePath, out _);
            
            if (probe.IsEmpty) {
                continue;
            }

            version = v;
            return true;
        }

        version = 0;
        return false;
    }

    private static Dictionary<int, long>? FindDiffRow(string optionName, long value)
    {
        return !MaterialDiff.TryGetValue(optionName, out var rows) ? null
            : rows.FirstOrDefault(row => row.ContainsValue(value));
    }

    private static long ValueForVersion(Dictionary<int, long> row, int userVersion)
    {
        if (row.TryGetValue(userVersion, out var x)) {
            return x;
        }

        var best = int.MinValue;

        foreach (var k in row.Keys.Where(k => k <= userVersion && k > best)) {
            best = k;
        }

        if (best != int.MinValue) {
            return row[best];
        }

        best = row.Keys.Prepend(int.MaxValue).Min();
        return row[best];
    }

    private static bool TryGetOptionValue(Material mat, string optionName, out long value)
    {
        value = 0;
        var opts = mat.ShaderAssign?.ShaderOptions;

        if (opts is not { Count: > 0 }) {
            return false;
        }

        foreach (var kv in opts) {
            if (string.Equals(kv.Key, optionName, StringComparison.OrdinalIgnoreCase)) {
                return TryParseOptionString(kv.Value?.String, out value);
            }
        }

        return false;
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
            && ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx)) {
            value = unchecked((long)hx);
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

    private static void SetOptionValue(Material mat, string optionName, long value)
    {
        var opts = mat.ShaderAssign?.ShaderOptions;

        if (opts is null) {
            return;
        }

        foreach (var kv in opts) {
            if (!string.Equals(kv.Key, optionName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            
            kv.Value.String = value.ToString(CultureInfo.InvariantCulture);
            return;
        }
    }

    private static Dictionary<string, List<Dictionary<int, long>>> LoadMaterialDiff(out int[] shaderVersions)
    {
        using var stream = typeof(BfresMcMerger).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.MaterialDiff.json")!;
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, List<Dictionary<int, long>>>(StringComparer.Ordinal);
        var versions = new List<int>();

        foreach (var prop in doc.RootElement.EnumerateObject()) {
            var rows = new List<Dictionary<int, long>>();
            
            foreach (var item in prop.Value.EnumerateArray()) {
                var row = new Dictionary<int, long>();
                
                foreach (var kv in item.EnumerateObject()) {
                    var vk = int.Parse(kv.Name, CultureInfo.InvariantCulture);
                    row[vk] = kv.Value.GetInt64();
                    versions.Add(vk);
                }
                
                rows.Add(row);
            }
            result[prop.Name] = rows;
        }

        shaderVersions = versions.Distinct().OrderDescending().ToArray();
        return result;
    }

    private static byte[] DecompressMcpk(ReadOnlySpan<byte> data)
    {
        if (data.Length < HEADER_SIZE) {
            throw new InvalidDataException("MCPK file too small.");
        }
        
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != MC_PK_MAGIC_LE) {
            throw new InvalidDataException("Not a MCPK file.");
        }

        var flags      = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var decompSize = (uint)((flags >> 5) << (flags & 0xF));
        
        if (decompSize == 0) {
            throw new InvalidDataException("MCPK header declares zero decompressed size.");
        }

        var output = new byte[(int)decompSize];
        using var dec = new Decompressor();
        dec.SetParameter(ZSTD_dParameter.ZSTD_d_experimentalParam1, (int)ZSTD_format_e.ZSTD_f_zstd1_magicless);

        try {
            dec.Unwrap(data[HEADER_SIZE..], output);
        }
        catch (ZstdException) { }

        return output;
    }

    private static byte[] CompressMcpk(byte[] src)
    {
        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(MC_PK_MAGIC_LE);
        writer.Write((byte)1); writer.Write((byte)1); writer.Write((byte)0); writer.Write((byte)0);
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
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag,    0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_dictIDFlag,      0);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_experimentalParam2, 1);
        return comp.Wrap(src).ToArray();
    }
}
