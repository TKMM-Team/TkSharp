using System.Globalization;
using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal sealed class AsbDocument
{
    public uint Version { get; set; }
    public string Filename { get; set; } = "";
    public JsonObject LocalBlackboard { get; set; } = [];
    public JsonArray Commands { get; set; } = [];
    public JsonArray Transitions { get; set; } = [];
    public JsonArray AnimationSlots { get; set; } = [];
    public SortedDictionary<uint, JsonObject> Nodes { get; set; } = [];
    public JsonArray ValidTags { get; set; } = [];
    public JsonArray X68 { get; set; } = [];
    public byte[]? Exb { get; set; }

    public AsbDocument DeepClone()
    {
        return new AsbDocument {
            Version = Version,
            Filename = Filename,
            LocalBlackboard = LocalBlackboard.CloneObject(),
            Commands = Commands.CloneArray(),
            Transitions = Transitions.CloneArray(),
            AnimationSlots = AnimationSlots.CloneArray(),
            Nodes = new SortedDictionary<uint, JsonObject>(
                Nodes.ToDictionary(x => x.Key, x => x.Value.CloneObject())),
            ValidTags = ValidTags.CloneArray(),
            X68 = X68.CloneArray(),
            Exb = Exb?.ToArray()
        };
    }
}

internal sealed record AsbHeader(
    uint Version,
    uint FilenameOffset,
    uint CommandCount,
    uint NodeCount,
    uint EventCount,
    uint SlotCount,
    uint X38Count,
    uint LocalBlackboardOffset,
    uint StringPoolOffset,
    uint EnumResolveArrayOffset,
    uint X2cOffset,
    uint EventOffsetsOffset,
    uint SlotsOffset,
    uint X38Offset,
    uint X38IndexOffset,
    uint X40Offset,
    uint X40Count,
    uint BoneGroupOffset,
    uint BoneGroupCount,
    uint StringPoolSize,
    uint TransitionsOffset,
    uint TagListOffset,
    uint MarkingsOffset,
    uint ExbOffset,
    uint CommandGroupsOffset,
    uint? X68Offset);

internal enum AsbParameterType
{
    String,
    Int,
    Float,
    Bool,
    Vec3
}

internal static class AsbJson
{
    public static JsonObject CloneObject(this JsonObject value) => (JsonObject)value.DeepClone();
    public static JsonArray CloneArray(this JsonArray value) => (JsonArray)value.DeepClone();

    public static JsonObject Object(JsonNode? node, string context) =>
        node as JsonObject ?? throw new InvalidDataException($"Expected object for {context}.");

    public static JsonArray Array(JsonNode? node, string context) =>
        node as JsonArray ?? throw new InvalidDataException($"Expected array for {context}.");

    public static JsonNode Required(this JsonObject value, string key) =>
        value[key] ?? throw new InvalidDataException($"Missing ASB field '{key}'.");

    public static string String(JsonNode? value)
    {
        if (value is JsonValue json && json.TryGetValue<string>(out var result)) {
            return result;
        }

        throw new InvalidDataException("Expected ASB string.");
    }

    public static bool Bool(JsonNode? value)
    {
        if (value is JsonValue json && json.TryGetValue<bool>(out var result)) {
            return result;
        }

        throw new InvalidDataException("Expected ASB boolean.");
    }

    public static uint UInt32(JsonNode? value)
    {
        if (value is JsonValue json) {
            if (json.TryGetValue<uint>(out var u32)) {
                return u32;
            }

            if (json.TryGetValue<ushort>(out var u16)) {
                return u16;
            }

            if (json.TryGetValue<byte>(out var u8)) {
                return u8;
            }

            if (json.TryGetValue<int>(out var i32)) {
                return unchecked((uint)i32);
            }

            if (json.TryGetValue<long>(out var i64)) {
                return unchecked((uint)i64);
            }

            if (json.TryGetValue<ulong>(out var u64)) {
                return checked((uint)u64);
            }
        }

        throw new InvalidDataException("Expected ASB integer.");
    }

    public static int Int32(JsonNode? value)
    {
        if (value is JsonValue json) {
            if (json.TryGetValue<ushort>(out var u16)) {
                return u16;
            }

            if (json.TryGetValue<short>(out var i16)) {
                return i16;
            }

            if (json.TryGetValue<int>(out var i32)) {
                return i32;
            }

            if (json.TryGetValue<uint>(out var u32)) {
                return unchecked((int)u32);
            }

            if (json.TryGetValue<long>(out var i64)) {
                return checked((int)i64);
            }
        }

        throw new InvalidDataException("Expected ASB integer.");
    }

    public static float Single(JsonNode? value)
    {
        if (value is JsonValue json) {
            if (json.TryGetValue<float>(out var f32)) {
                return f32;
            }

            if (json.TryGetValue<double>(out var f64)) {
                return (float)f64;
            }

            if (json.TryGetValue<int>(out var i32)) {
                return i32;
            }

            if (json.TryGetValue<uint>(out var u32)) {
                return u32;
            }
        }

        throw new InvalidDataException("Expected ASB number.");
    }

    public static string Hex(uint value) => $"0x{value:x}";

    public static uint ParseHex(string value)
    {
        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan();
        return uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static JsonArray Strings(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values) {
            result.Add(value);
        }

        return result;
    }

    public static List<string> StringList(JsonNode? value)
    {
        if (value is not JsonArray array) {
            return [];
        }

        return array.Select(String).ToList();
    }

    public static int IndexOfDeep(JsonArray values, JsonNode wanted)
    {
        for (var i = 0; i < values.Count; i++) {
            if (JsonNode.DeepEquals(values[i], wanted)) {
                return i;
            }
        }

        return -1;
    }

    public static void AddUnique(JsonArray values, JsonNode value)
    {
        if (IndexOfDeep(values, value) < 0) {
            values.Add(value.DeepClone());
        }
    }

    public static bool BytesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
