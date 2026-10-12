using System.Globalization;
using System.Text.Json;
using BfresLibrary;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Extensions;

namespace TkSharp.Merging.Bfres;

internal static class BfresUpdater
{
    private const string MATERIAL_PRODUCT_CANONICAL = "Shader/material.Product.product.Nin_NX_NVN.bfsha";

    private static readonly string[] ExpressionOptions = [
        "o_expression_pre_normal",
        "o_expression_post_normal"
    ];

    private static readonly Dictionary<string, List<Dictionary<int, long>>> MaterialDiff = LoadMaterialDiff();

    public static bool Apply(ResFile resFile, ITkRom rom)
    {
        return TryGetShaderProductVersion(rom, out var userVersion)
               && ApplyMaterialDiff(resFile, userVersion);
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

        var sourceVersion = row.First(kv => kv.Value == current).Key;
        var next = ValueForVersion(row, userVersion);
        if (next == current) {
            return false;
        }

        TkLog.Instance.LogInformation(
            "The material '{MaterialName}' in model '{ModelName}' corresponds to shaders version "
            + "'{SourceVersion}' and will be {Action} for version '{TargetVersion}'.",
            mat.Name,
            model.Name,
            sourceVersion,
            userVersion > sourceVersion ? "updated" : "downgraded",
            userVersion);

        option.String = next.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryGetShaderProductVersion(ITkRom rom, out int version)
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
        using var stream = typeof(BfresUpdater).Assembly
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
}
