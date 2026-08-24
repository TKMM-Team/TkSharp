using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    private static readonly string[] NodeTypes = [
        "", "FloatSelector", "StringSelector", "SkeletalAnimation", "State", "Unknown2",
        "OneDimensionalBlender", "Sequential", "IntSelector", "Simultaneous", "Event",
        "MaterialAnimation", "FrameController", "DummyAnimation", "RandomSelector", "Unknown4",
        "PreviousTagSelector", "BonePositionSelector", "BoneAnimation", "InitialFrame",
        "BoneBlender", "BoolSelector", "Alert", "SubtractAnimation", "ShapeAnimation", "Unknown7"
    ];

    public static AsbDocument Read(ReadOnlyMemory<byte> data)
    {
        var reader = new BinaryDataReader(data);
        var header = ReadHeader(reader, data.Length);
        var poolBase = checked((int)header.StringPoolOffset);
        var document = new AsbDocument {
            Version = header.Version,
            Filename = reader.ReadCStringAt(poolBase + header.FilenameOffset)
        };

        reader.Seek(header.LocalBlackboardOffset);
        document.LocalBlackboard = ReadBlackboard(reader, poolBase);

        reader.Seek(header.Version == 0x417 ? 0x6c : 0x68);
        for (var i = 0u; i < header.CommandCount; i++) {
            document.Commands.Add(ReadCommand(reader, poolBase, header.Version));
        }

        var nodeStart = reader.Position;
        var commandGroups = ReadCommandGroups(reader, poolBase, header.CommandGroupsOffset);
        document.Transitions = ReadTransitions(reader, poolBase, header.TransitionsOffset, commandGroups);

        reader.Seek(header.SlotsOffset);
        for (var i = 0u; i < header.SlotCount; i++) {
            document.AnimationSlots.Add(ReadAnimationSlot(reader, poolBase));
        }

        var events = ReadEvents(reader, poolBase, header.EventOffsetsOffset, header.EventCount);
        var x2c = ReadX2c(reader, poolBase, header.X2cOffset);
        document.ValidTags = ReadTags(reader, poolBase, header.TagListOffset);
        document.X68 = ReadX68(reader, poolBase, header.X68Offset);
        var x38 = ReadX38(reader, poolBase, header.X38Offset, header.X38Count);
        var x40 = ReadX40(reader, header.X40Offset, header.X40Count, header.Version);
        var bones = ReadBones(reader, poolBase, header.BoneGroupOffset, header.BoneGroupCount);
        var markings = ReadMarkings(reader, poolBase, header.MarkingsOffset);

        if (header.ExbOffset != 0) {
            var end = header.MarkingsOffset > header.ExbOffset
                ? header.MarkingsOffset
                : checked((uint)data.Length);
            document.Exb = data.Slice(checked((int)header.ExbOffset), checked((int)(end - header.ExbOffset))).ToArray();
            if (document.Exb.Length < 4 || !document.Exb.AsSpan(0, 4).SequenceEqual("EXB "u8)) {
                throw new InvalidDataException("Invalid embedded EXB section.");
            }
        }

        reader.Position = nodeStart;
        for (var index = 0u; index < header.NodeCount; index++) {
            try {
                document.Nodes[index] = ReadNode(reader, poolBase, x38, x40, markings,
                    header.X38IndexOffset, header.Version, x2c, events, bones);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException) {
                throw new InvalidDataException($"ASB node {index}: {exception.Message}", exception);
            }
        }

        return document;
    }

    private static AsbHeader ReadHeader(BinaryDataReader reader, int length)
    {
        if (reader.ReadFixedString(4) != "ASB ") {
            throw new InvalidDataException("Invalid ASB magic.");
        }

        var version = reader.ReadUInt32();
        if (version is not (0x40f or 0x417)) {
            throw new InvalidDataException($"Unsupported ASB version 0x{version:x}.");
        }

        var header = new AsbHeader(
            version,
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            version == 0x417 ? reader.ReadUInt32() : null);

        foreach (var offset in new uint[] {
                     header.LocalBlackboardOffset, header.StringPoolOffset, header.EnumResolveArrayOffset,
                     header.X2cOffset, header.EventOffsetsOffset, header.SlotsOffset, header.X38Offset,
                     header.X38IndexOffset, header.X40Offset, header.BoneGroupOffset, header.TransitionsOffset,
                     header.TagListOffset, header.MarkingsOffset, header.ExbOffset, header.CommandGroupsOffset,
                     header.X68Offset ?? 0
                 }) {
            if (offset > length) {
                throw new InvalidDataException($"ASB section offset 0x{offset:x} exceeds file size 0x{length:x}.");
            }
        }

        if ((ulong)header.StringPoolOffset + header.StringPoolSize > (ulong)length) {
            throw new InvalidDataException("ASB string pool exceeds file.");
        }

        return header;
    }

    private static JsonObject ReadCommand(BinaryDataReader reader, int poolBase, uint version)
    {
        var result = new JsonObject {
            ["Name"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32())
        };
        if (version == 0x417) {
            var tags = ReadOptionalTags(reader, poolBase, reader.ReadUInt32());
            if (tags is not null) {
                result["Tags"] = tags;
            }
        }

        result["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
        result["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
        result["Unknown 3"] = reader.ReadUInt32();
        result["GUID"] = ReadGuid(reader);
        result["Left Node Index"] = reader.ReadUInt16();
        result["Right Node Index"] = (int)reader.ReadUInt16() - 1;
        return result;
    }

    private static JsonObject ReadNode(BinaryDataReader reader, int poolBase, JsonArray x38, JsonArray x40,
        JsonArray markings, uint x38IndexOffset, uint version, JsonArray x2c, JsonArray events, JsonArray bones)
    {
        var typeIndex = reader.ReadUInt16();
        if (typeIndex == 0 || typeIndex >= NodeTypes.Length) {
            throw new InvalidDataException($"Invalid ASB node type {typeIndex}.");
        }

        var x38Count = reader.ReadByte();
        var unknown = reader.ReadByte();
        var tagOffset = reader.ReadUInt32();
        var tags = ReadOptionalTags(reader, poolBase, tagOffset);
        var bodyOffset = reader.ReadUInt32();
        var x40Index = reader.ReadUInt16();
        var x40Count = reader.ReadUInt16();
        var x38Index = reader.ReadUInt16();
        var markingIndex = (int)reader.ReadUInt16() - 1;
        var guid = ReadGuid(reader);
        var returnPosition = reader.Position;

        if ((uint)x40Index + x40Count > x40.Count) {
            throw new InvalidDataException("ASB node 0x40 range exceeds table.");
        }

        var nodeX40 = new JsonArray();
        for (var i = 0; i < x40Count; i++) {
            nodeX40.Add(x40[x40Index + i]!.DeepClone());
        }

        var nodeX38 = new JsonArray();
        if (x38Count > 0) {
            reader.Seek(x38IndexOffset + 4u * x38Index);
            for (var i = 0; i < x38Count; i++) {
                var index = reader.ReadUInt32();
                if (index >= x38.Count) {
                    throw new InvalidDataException("ASB node 0x38 index exceeds table.");
                }

                nodeX38.Add(x38[(int)index]!.DeepClone());
            }
        }

        var type = NodeTypes[typeIndex];
        reader.Seek(bodyOffset);
        var body = ReadBody(type, reader, poolBase, version, x2c, events, bones);
        reader.Position = returnPosition;

        var result = new JsonObject {
            ["Node Type"] = type,
            ["Unknown"] = unknown,
            ["GUID"] = guid,
            ["0x38 Entries"] = nodeX38,
            ["0x40 Entries"] = nodeX40
        };
        if (tags is not null) {
            result["Tags"] = tags;
        }

        if (markingIndex >= 0) {
            if (markingIndex >= markings.Count) {
                throw new InvalidDataException("ASB marking index exceeds table.");
            }

            result["ASMarkings"] = markings[markingIndex]!.DeepClone();
        }

        if (body is not null) {
            result["Body"] = body;
        }

        return result;
    }

    private static JsonNode ReadParameter(BinaryDataReader reader, int poolBase, AsbParameterType type)
    {
        var flags = reader.ReadInt32();
        if (flags >= 0) {
            return ReadParameterValue(reader, poolBase, type);
        }

        var bits = unchecked((uint)flags);
        var index = bits & 0xffff;
        var flag = (bits & 0xffff0000) >> 16;
        var result = new JsonObject();
        if (((bits ^ uint.MaxValue) & 0x81000000) == 0) {
            result["EXB Index"] = index;
        }
        else if (type is not (AsbParameterType.Float or AsbParameterType.Vec3)) {
            result["Flags"] = AsbJson.Hex(flag);
            result["Type"] = ParameterTypeName(type);
            result["Local Blackboard Index"] = index;
        }
        else if ((flag >> 14) < 3 || ((flag >> 8) & 1) != 0) {
            result["Flags"] = AsbJson.Hex(flag);
            if (((flag >> 9) & 1) == 0) {
                result["Type"] = ParameterTypeName(type);
                result["Local Blackboard Index"] = index;
            }
            else {
                result["Index"] = index;
            }
        }
        else {
            result["Flags"] = AsbJson.Hex(flag);
            result["Index"] = index;
        }

        var defaultValue = ReadParameterValue(reader, poolBase, type);
        if (Truthy(defaultValue)) {
            result["Default Value"] = defaultValue;
        }

        return result;
    }

    private static JsonNode ReadParameterValue(BinaryDataReader reader, int poolBase, AsbParameterType type)
    {
        return (type switch {
            AsbParameterType.String => ReadParameterString(reader, poolBase),
            AsbParameterType.Int => JsonValue.Create(reader.ReadInt32()),
            AsbParameterType.Float => JsonValue.Create(reader.ReadSingle()),
            AsbParameterType.Bool => JsonValue.Create(reader.ReadUInt32() != 0),
            AsbParameterType.Vec3 => new JsonArray(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            _ => throw new InvalidDataException("Invalid ASB parameter type.")
        })!;
    }

    private static JsonNode ReadParameterString(BinaryDataReader reader, int poolBase)
    {
        var offset = reader.ReadUInt32();
        return JsonValue.Create(offset >= reader.Length - poolBase
            ? ""
            : reader.ReadCStringAt(poolBase + offset))!;
    }

    private static bool Truthy(JsonNode value)
    {
        if (value is JsonArray array) {
            return array.Count > 0;
        }

        if (value is JsonObject obj) {
            return obj.Count > 0;
        }

        if (value is JsonValue json) {
            if (json.TryGetValue<bool>(out var boolean)) return boolean;
            if (json.TryGetValue<string>(out var text)) return text.Length > 0;
            if (json.TryGetValue<int>(out var integer)) return integer != 0;
            if (json.TryGetValue<uint>(out var unsigned)) return unsigned != 0;
            if (json.TryGetValue<float>(out var number)) return number != 0;
            if (json.TryGetValue<double>(out var number64)) return number64 != 0;
        }

        return false;
    }

    private static string ParameterTypeName(AsbParameterType type) => type switch {
        AsbParameterType.String => "string",
        AsbParameterType.Int => "int",
        AsbParameterType.Float => "float",
        AsbParameterType.Bool => "bool",
        AsbParameterType.Vec3 => "vec3f",
        _ => throw new InvalidDataException("Invalid ASB parameter type.")
    };

    private static string ReadGuid(BinaryDataReader reader)
    {
        var first = reader.ReadUInt32();
        var second = reader.ReadUInt16();
        var third = reader.ReadUInt16();
        var fourth = reader.ReadUInt16();
        var tail = Convert.ToHexString(reader.ReadBytes(6)).ToLowerInvariant();
        return $"{first:x8}-{second:x4}-{third:x4}-{fourth:x4}-{tail}";
    }

    private static JsonArray? ReadOptionalTags(BinaryDataReader reader, int poolBase, uint offset)
    {
        if (offset == 0) {
            return null;
        }

        var returnPosition = reader.Position;
        var result = ReadTags(reader, poolBase, offset);
        reader.Position = returnPosition;
        return result;
    }
}
