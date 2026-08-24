using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    public static byte[] Write(AsbDocument document) => new AsbWriter(document).Write();

    private sealed partial class AsbWriter
    {
        private readonly AsbDocument _document;
        private readonly uint _version;
        private readonly StringPool _strings = new();
        private readonly JsonArray _events = [];
        private readonly JsonArray _x2c = [];
        private readonly JsonArray _bones = [];
        private readonly JsonArray _markings = [];
        private readonly JsonArray _x38 = [];
        private readonly JsonArray _x40 = [];
        private readonly JsonArray _tagGroups = [];

        public AsbWriter(AsbDocument document)
        {
            _document = document;
            _version = document.Version;
            if (_version is not (0x40f or 0x417)) {
                throw new InvalidDataException($"Unsupported ASB version 0x{_version:x}.");
            }

            ReconstructHiddenTables();
        }

        public byte[] Write()
        {
            var writer = new BinaryDataWriter();
            writer.Write("ASB "u8);
            writer.Write(_version);
            writer.Write(_strings.Offset(_document.Filename));
            writer.Write(checked((uint)_document.Commands.Count));
            writer.Write(checked((uint)_document.Nodes.Count));
            writer.Write(checked((uint)_events.Count));
            writer.Write(checked((uint)_document.AnimationSlots.Count));
            writer.Write(checked((uint)_x38.Count));
            var headerPatches = ReserveUInt32(writer, _version == 0x417 ? 19 : 18);
            var tagPatches = new List<(int Position, JsonArray Tags)>();

            foreach (var commandNode in _document.Commands) {
                var command = AsbJson.Object(commandNode, "command");
                writer.Write(_strings.Offset(AsbJson.String(command.Required("Name"))));
                if (_version == 0x417) {
                    WriteTagReference(writer, command["Tags"] as JsonArray, tagPatches);
                }

                WriteParameter(writer, command.Required("Unknown 1"));
                WriteParameter(writer, command.Required("Unknown 2"));
                writer.Write(AsbJson.UInt32(command.Required("Unknown 3")));
                WriteGuid(writer, AsbJson.String(command.Required("GUID")));
                writer.Write(checked((ushort)AsbJson.UInt32(command.Required("Left Node Index"))));
                writer.Write(checked((ushort)(AsbJson.Int32(command.Required("Right Node Index")) + 1)));
            }

            var bodyPatches = new List<int>();
            ushort x40Index = 0;
            ushort x38Index = 0;
            foreach (var node in _document.Nodes.Values) {
                var typeName = AsbJson.String(node.Required("Node Type"));
                var typeIndex = Array.IndexOf(NodeTypes, typeName);
                if (typeIndex <= 0) {
                    throw new InvalidDataException($"Unsupported ASB node type {typeName}.");
                }

                var nodeX38 = AsbJson.Array(node["0x38 Entries"], "node 0x38 entries");
                var nodeX40 = AsbJson.Array(node["0x40 Entries"], "node 0x40 entries");
                writer.Write(checked((ushort)typeIndex));
                writer.Write(checked((byte)nodeX38.Count));
                writer.Write(checked((byte)AsbJson.UInt32(node.Required("Unknown"))));
                WriteTagReference(writer, node["Tags"] as JsonArray, tagPatches);
                bodyPatches.Add(writer.ReserveUInt32());
                writer.Write(x40Index);
                writer.Write(checked((ushort)nodeX40.Count));
                x40Index = checked((ushort)(x40Index + nodeX40.Count));
                writer.Write(x38Index);
                x38Index = checked((ushort)(x38Index + nodeX38.Count));
                var marking = node["ASMarkings"] is JsonArray nodeMarking
                    ? AsbJson.IndexOfDeep(_markings, nodeMarking) + 1
                    : 0;
                writer.Write(checked((ushort)marking));
                WriteGuid(writer, AsbJson.String(node.Required("GUID")));
            }

            var eventOffsetsPosition = writer.Position;
            var eventPatches = ReserveUInt32(writer, _events.Count);
            writer.PatchUInt32(headerPatches[4], checked((uint)eventOffsetsPosition));

            var nodePosition = 0;
            foreach (var node in _document.Nodes.Values) {
                writer.PatchUInt32(bodyPatches[nodePosition++], checked((uint)writer.Position));
                if (node["Body"] is JsonObject body) {
                    WriteBody(writer, AsbJson.String(node.Required("Node Type")), body, tagPatches);
                }
            }

            var x38IndexOffset = writer.Position;
            foreach (var node in _document.Nodes.Values) {
                foreach (var entry in AsbJson.Array(node["0x38 Entries"], "node 0x38 entries")) {
                    var index = AsbJson.IndexOfDeep(_x38, entry!);
                    if (index < 0) {
                        throw new InvalidDataException("ASB 0x38 table entry missing.");
                    }

                    writer.Write(checked((uint)index));
                }
            }

            var x38Offset = writer.Position;
            var x38ValueOffset = checked(x38Offset + 0x18 * _x38.Count);
            foreach (var entryNode in _x38) {
                var entry = AsbJson.Object(entryNode, "0x38 entry");
                var type = AsbJson.UInt32(entry.Required("Type"));
                writer.Write(type);
                writer.Write(checked((uint)x38ValueOffset));
                WriteGuid(writer, AsbJson.String(entry.Required("GUID")));
                x38ValueOffset += type switch {
                    0 => 12,
                    1 => 24,
                    _ => 0
                };
            }

            foreach (var entryNode in _x38) {
                var entry = AsbJson.Object(entryNode, "0x38 entry");
                var type = AsbJson.UInt32(entry.Required("Type"));
                if (type is not (0 or 1)) {
                    continue;
                }

                var value = AsbJson.Object(entry.Required("Entry"), "0x38 value");
                WriteParameter(writer, value.Required("Start Frame"));
                if (type == 0) {
                    writer.Write(AsbJson.UInt32(value.Required("Unknown 2")));
                }
                else {
                    WriteParameter(writer, value.Required("End Frame"));
                    WriteParameter(writer, value.Required("Unknown 3"));
                }
            }

            var x2cOffset = writer.Position;
            writer.Write(checked((uint)_x2c.Count));
            foreach (var entryNode in _x2c) {
                var entry = AsbJson.Object(entryNode, "0x2C entry");
                writer.Write(checked((ushort)AsbJson.UInt32(entry.Required("Source Node"))));
                writer.Write(checked((ushort)AsbJson.UInt32(entry.Required("Target Node"))));
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 1")));
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 2")));
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 3")));
                foreach (var subNode in AsbJson.Array(entry.Required("Entries"), "0x2C sub entries")) {
                    var sub = AsbJson.Object(subNode, "0x2C sub entry");
                    var type = AsbJson.UInt32(sub.Required("Entry Type"));
                    writer.Write(checked((ushort)type));
                    writer.Write(checked((ushort)AsbJson.UInt32(sub.Required("Unknown Type"))));
                    if (type == 0) {
                        writer.Write(new byte[16]);
                    }
                    else {
                        WriteParameter(writer, sub["Unknown 1"]);
                        WriteParameter(writer, sub["Unknown 2"]);
                    }
                }
            }

            for (var i = 0; i < _events.Count; i++) {
                writer.PatchUInt32(eventPatches[i], checked((uint)writer.Position));
                WriteEvent(writer, AsbJson.Object(_events[i], "event"));
            }

            var (transitionsOffset, commandGroupsOffset) = WriteTransitions(writer);
            var blackboardOffset = WriteBlackboard(writer);
            var slotsOffset = WriteSlots(writer);
            var bonesOffset = WriteBones(writer);

            var x40Offset = writer.Position;
            foreach (var entryNode in _x40) {
                var entry = AsbJson.Object(entryNode, "0x40 entry");
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 1")));
                writer.Write(AsbJson.Single(entry.Required("Angle")));
                if (_version == 0x417) {
                    writer.Write(entry["Type"] is null ? 0u : AsbJson.UInt32(entry["Type"]));
                }

                writer.Write(AsbJson.Single(entry.Required("Unknown 2")));
                writer.Write(AsbJson.Single(entry.Required("Rate")));
                writer.Write(AsbJson.Single(entry.Required("Unknown 3")));
                writer.Write(AsbJson.Single(entry.Required("Min")));
                writer.Write(AsbJson.Single(entry.Required("Max")));
            }

            var tagListOffset = writer.Position;
            writer.Write(checked((uint)_document.ValidTags.Count));
            foreach (var tag in _document.ValidTags) {
                writer.Write(_strings.Offset(AsbJson.String(tag)));
            }

            foreach (var groupNode in _tagGroups) {
                var group = AsbJson.Array(groupNode, "tag group");
                var position = checked((uint)writer.Position);
                foreach (var patch in tagPatches.Where(x => JsonNode.DeepEquals(x.Tags, group))) {
                    writer.PatchUInt32(patch.Position, position);
                }

                writer.Write(checked((uint)group.Count));
                foreach (var tag in group) {
                    writer.Write(_strings.Offset(AsbJson.String(tag)));
                }
            }

            var exbOffset = 0u;
            if (_document.Exb is not null) {
                exbOffset = checked((uint)writer.Position);
                writer.Write(_document.Exb);
            }

            var markingsOffset = writer.Position;
            writer.Write(checked((uint)_markings.Count));
            foreach (var groupNode in _markings) {
                var group = AsbJson.Array(groupNode, "marking");
                if (group.Count != 3) {
                    throw new InvalidDataException("ASB marking must contain three strings.");
                }

                foreach (var value in group) {
                    writer.Write(_strings.Offset(AsbJson.String(value)));
                }
            }

            var x68Offset = writer.Position;
            if (_version == 0x417) {
                writer.Write(checked((uint)_document.X68.Count));
                foreach (var entryNode in _document.X68) {
                    var entry = AsbJson.Object(entryNode, "0x68 entry");
                    writer.Write(_strings.Offset(AsbJson.String(entry.Required("Name"))));
                    writer.Write(AsbJson.Single(entry.Required("Unknown")));
                }
            }

            var enumOffset = writer.Position;
            writer.Write(0u);
            var stringsOffset = writer.Position;
            writer.Write(_strings.Bytes);

            uint[] headerValues = [
                blackboardOffset,
                checked((uint)stringsOffset),
                checked((uint)enumOffset),
                checked((uint)x2cOffset),
                checked((uint)eventOffsetsPosition),
                slotsOffset,
                checked((uint)x38Offset),
                checked((uint)x38IndexOffset),
                checked((uint)x40Offset),
                checked((uint)_x40.Count),
                bonesOffset,
                checked((uint)_bones.Count),
                checked((uint)_strings.Bytes.Length),
                transitionsOffset,
                checked((uint)tagListOffset),
                checked((uint)markingsOffset),
                exbOffset,
                commandGroupsOffset
            ];
            for (var i = 0; i < headerValues.Length; i++) {
                writer.PatchUInt32(headerPatches[i], headerValues[i]);
            }

            if (_version == 0x417) {
                writer.PatchUInt32(headerPatches[18], checked((uint)x68Offset));
            }

            return writer.ToArray();
        }

        private void ReconstructHiddenTables()
        {
            foreach (var node in _document.Nodes.Values) {
                foreach (var entry in AsbJson.Array(node["0x38 Entries"], "node 0x38 entries")) {
                    AsbJson.AddUnique(_x38, entry!);
                }

                foreach (var entry in AsbJson.Array(node["0x40 Entries"], "node 0x40 entries")) {
                    _x40.Add(entry!.DeepClone());
                }

                AddTagGroup(node["Tags"] as JsonArray);
                if (node["ASMarkings"] is JsonArray marking) {
                    AsbJson.AddUnique(_markings, marking);
                }

                if (node["Body"] is not JsonObject body) {
                    continue;
                }

                var kind = AsbJson.String(node.Required("Node Type"));
                if (kind == "Event" && body["Event"] is JsonObject eventNode) {
                    AsbJson.AddUnique(_events, eventNode);
                }

                if (kind == "PreviousTagSelector" && body["Child Nodes"] is JsonArray previousChildren) {
                    foreach (var childNode in previousChildren) {
                        AddTagGroup(AsbJson.Object(childNode, "previous-tag child")["Tags"] as JsonArray);
                    }
                }

                if (kind == "InitialFrame") {
                    AddTagGroup(body["Tags"] as JsonArray);
                }

                if (kind == "BoneBlender" && body["Bone Group"] is JsonObject boneGroup) {
                    AsbJson.AddUnique(_bones, boneGroup);
                }

                if (body["0x2C Connections"] is JsonArray connections) {
                    foreach (var connectionNode in connections) {
                        if (connectionNode is not JsonObject connection ||
                            connection["0x2C Entry"] is not JsonObject entry ||
                            entry.Count == 0) {
                            continue;
                        }

                        AsbJson.AddUnique(_x2c, entry);
                    }
                }
            }

            foreach (var commandNode in _document.Commands) {
                AddTagGroup(AsbJson.Object(commandNode, "command")["Tags"] as JsonArray);
            }
        }

        private void AddTagGroup(JsonArray? tags)
        {
            if (tags is not null) {
                AsbJson.AddUnique(_tagGroups, tags);
            }
        }

        private static List<int> ReserveUInt32(BinaryDataWriter writer, int count)
        {
            var result = new List<int>(count);
            for (var i = 0; i < count; i++) {
                result.Add(writer.ReserveUInt32());
            }

            return result;
        }

        private static void WriteTagReference(BinaryDataWriter writer, JsonArray? tags,
            List<(int Position, JsonArray Tags)> patches)
        {
            if (tags is null) {
                writer.Write(0u);
                return;
            }

            patches.Add((writer.ReserveUInt32(), tags.CloneArray()));
        }

        private static void WriteGuid(BinaryDataWriter writer, string value)
        {
            var parts = value.Split('-');
            if (parts.Length != 5 || parts[0].Length != 8 || parts[1].Length != 4 ||
                parts[2].Length != 4 || parts[3].Length != 4 || parts[4].Length != 12) {
                throw new InvalidDataException($"Invalid ASB GUID {value}.");
            }

            writer.Write(uint.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(ushort.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(ushort.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(ushort.Parse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(Convert.FromHexString(parts[4]));
        }

        private void WriteParameter(BinaryDataWriter writer, JsonNode? value)
        {
            if (value is JsonObject reference) {
                var flags = reference["Flags"] is JsonNode flagNode
                    ? AsbJson.ParseHex(AsbJson.String(flagNode)) << 16
                    : 0x81000000;
                var indexNode = reference["Index"] ?? reference["Local Blackboard Index"] ?? reference["EXB Index"]
                    ?? throw new InvalidDataException("ASB parameter index missing.");
                writer.Write(flags | (AsbJson.UInt32(indexNode) & 0xffff));
                WriteParameterValue(writer, reference["Default Value"]);
                return;
            }

            writer.Write(0u);
            WriteParameterValue(writer, value);
        }

        private void WriteParameterValue(BinaryDataWriter writer, JsonNode? value)
        {
            if (value is null) {
                writer.Write(0u);
                return;
            }

            if (value is JsonArray vector) {
                foreach (var component in vector) {
                    writer.Write(AsbJson.Single(component));
                }

                return;
            }

            if (value is not JsonValue json) {
                throw new InvalidDataException("Invalid ASB parameter value.");
            }

            if (json.TryGetValue<bool>(out var boolean)) {
                writer.Write(boolean ? 1u : 0u);
            }
            else if (json.TryGetValue<string>(out var text)) {
                writer.Write(_strings.Offset(text));
            }
            else if (json.TryGetValue<int>(out var integer)) {
                writer.Write(integer);
            }
            else if (json.TryGetValue<uint>(out var unsigned)) {
                writer.Write(unsigned);
            }
            else if (json.TryGetValue<float>(out var number)) {
                writer.Write(number);
            }
            else if (json.TryGetValue<double>(out var number64)) {
                writer.Write((float)number64);
            }
            else if (json.TryGetValue<long>(out var integer64)) {
                writer.Write(checked((int)integer64));
            }
            else {
                throw new InvalidDataException("Invalid ASB parameter value.");
            }
        }

        private sealed class StringPool
        {
            private readonly Dictionary<string, uint> _offsets = new(StringComparer.Ordinal) { [""] = 0 };
            private readonly List<byte> _bytes = [0];

            public byte[] Bytes => [.. _bytes];

            public uint Offset(string value)
            {
                if (_offsets.TryGetValue(value, out var offset)) {
                    return offset;
                }

                offset = checked((uint)_bytes.Count);
                _bytes.AddRange(Encoding.UTF8.GetBytes(value));
                _bytes.Add(0);
                _offsets[value] = offset;
                return offset;
            }
        }
    }
}
