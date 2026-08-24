using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    private sealed record ConnectionData(List<uint> ChildOffsets, JsonObject Fields);

    private static JsonObject? ReadBody(string kind, BinaryDataReader reader, int poolBase, uint version,
        JsonArray x2c, JsonArray events, JsonArray bones)
    {
        var body = new JsonObject();
        switch (kind) {
            case "SkeletalAnimation":
                body["Animation"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 1"] = reader.ReadUInt32();
                body["Unknown 2"] = reader.ReadUInt32();
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 4"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                FinishBody(body, reader, version, x2c);
                break;
            case "Sequential":
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                FinishBody(body, reader, version, x2c);
                break;
            case "Simultaneous":
                body["Unknown"] = reader.ReadUInt32();
                FinishBody(body, reader, version, x2c);
                break;
            case "Event": {
                var index = reader.ReadUInt32();
                if (index >= events.Count) {
                    throw new InvalidDataException("ASB event index exceeds table.");
                }

                body["Event"] = events[(int)index]!.DeepClone();
                FinishBody(body, reader, version, x2c);
                break;
            }
            case "MaterialAnimation":
                if (version == 0x417) {
                    body["Unknown 1"] = reader.ReadUInt32();
                }

                body["Animation"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                FinishBody(body, reader, version, x2c);
                break;
            case "DummyAnimation":
                body["Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                FinishBody(body, reader, version, x2c);
                break;
            case "OneDimensionalBlender": {
                body["Parameter"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown"] = reader.ReadUInt32();
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                foreach (var offset in connections.ChildOffsets) {
                    reader.Seek(offset);
                    children.Add(new JsonObject {
                        ["Condition Min"] = ReadParameter(reader, poolBase, AsbParameterType.Float),
                        ["Condition Max"] = ReadParameter(reader, poolBase, AsbParameterType.Float),
                        ["Node Index"] = reader.ReadUInt32()
                    });
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "RandomSelector": {
                body["Unknown 1"] = reader.ReadUInt32();
                body["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                body["Unknown 4"] = reader.ReadUInt32() != 0;
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                foreach (var offset in connections.ChildOffsets) {
                    reader.Seek(offset);
                    children.Add(new JsonObject {
                        ["Weight"] = ReadParameter(reader, poolBase, AsbParameterType.Float),
                        ["Node Index"] = reader.ReadUInt32()
                    });
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "FrameController":
                body["Animation Rate"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Start Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["End Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown Flag"] = reader.ReadUInt32();
                body["Loop Cancel Flag"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                body["Unknown 4"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                body["Unknown 5"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 6"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown 7"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown 8"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown 9"] = reader.ReadUInt32() != 0;
                body["Unknown 10"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                if (version == 0x417) {
                    body["Unknown 11"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                }

                body["Unknown 12"] = reader.ReadUInt32();
                body["Unknown 13"] = reader.ReadUInt32();
                FinishBody(body, reader, version, x2c);
                break;
            case "BoneAnimation":
                body["Animation"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = reader.ReadUInt32();
                body["Unknown 3"] = reader.ReadUInt32();
                body["Unknown 4"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                FinishBody(body, reader, version, x2c);
                break;
            case "Alert":
                body["Message"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                FinishBody(body, reader, version, x2c);
                break;
            case "ShapeAnimation":
                body["Animation"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                FinishBody(body, reader, version, x2c);
                break;
            case "StringSelector":
                body["Parameter"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = reader.ReadUInt32() != 0;
                ReadSimpleSelectorChildren(body, reader, poolBase, version, x2c, AsbParameterType.String);
                break;
            case "FloatSelector": {
                body["Parameter"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = reader.ReadUInt32() != 0;
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                for (var i = 0; i < connections.ChildOffsets.Count; i++) {
                    reader.Seek(connections.ChildOffsets[i]);
                    var child = new JsonObject();
                    if (i + 1 == connections.ChildOffsets.Count) {
                        child["Default Condition"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                        reader.Skip(8);
                    }
                    else {
                        child["Condition Min"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                        child["Condition Max"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                    }

                    child["Node Index"] = reader.ReadUInt32();
                    children.Add(child);
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "IntSelector": {
                body["Parameter"] = ReadParameter(reader, poolBase, AsbParameterType.Int);
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = reader.ReadUInt32() != 0;
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                for (var i = 0; i < connections.ChildOffsets.Count; i++) {
                    reader.Seek(connections.ChildOffsets[i]);
                    children.Add(new JsonObject {
                        [i + 1 == connections.ChildOffsets.Count ? "Default Condition" : "Condition"] =
                            ReadParameter(reader, poolBase, AsbParameterType.Int),
                        ["Node Index"] = reader.ReadUInt32()
                    });
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "BoolSelector": {
                body["Parameter"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 2"] = reader.ReadUInt32() != 0;
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                for (var i = 0; i < connections.ChildOffsets.Count; i++) {
                    reader.Seek(connections.ChildOffsets[i]);
                    children.Add(new JsonObject {
                        [i == 0 ? "Condition True" : "Condition False"] = reader.ReadUInt32()
                    });
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "PreviousTagSelector": {
                body["Unknown"] = reader.ReadUInt32();
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                foreach (var offset in connections.ChildOffsets) {
                    reader.Seek(offset);
                    var tagOffset = reader.ReadUInt32();
                    var nodeIndex = reader.ReadUInt32();
                    children.Add(new JsonObject {
                        ["Tags"] = tagOffset == uint.MaxValue ? new JsonArray() : ReadTagsPreserving(reader, poolBase, tagOffset),
                        ["Node Index"] = nodeIndex
                    });
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "BonePositionSelector": {
                body["Bone 1"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Bone 2"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 1"] = reader.ReadUInt32();
                body["Unknown 2"] = reader.ReadUInt32();
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                var connections = ReadConnections(reader, version, x2c);
                var children = new JsonArray();
                for (var i = 0; i < connections.ChildOffsets.Count; i++) {
                    reader.Seek(connections.ChildOffsets[i]);
                    var child = new JsonObject();
                    if (i + 1 == connections.ChildOffsets.Count) {
                        child["Default Condition"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                        reader.Skip(8);
                    }
                    else {
                        child["Condition Min"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                        child["Condition Max"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                    }

                    child["Node Index"] = reader.ReadUInt32();
                    children.Add(child);
                }

                AddChildren(body, children);
                MergeConnections(body, connections);
                break;
            }
            case "InitialFrame": {
                body["Flag"] = reader.ReadUInt32();
                var tagOffset = reader.ReadUInt32();
                if (tagOffset != 0) {
                    body["Tags"] = ReadTagsPreserving(reader, poolBase, tagOffset);
                }

                if (version == 0x417) {
                    body["Unknown 1"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                }

                body["Bone 1"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Bone 2"] = ReadParameter(reader, poolBase, AsbParameterType.String);
                body["Unknown 2"] = reader.ReadUInt32();
                body["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                body["Unknown 4"] = ReadParameter(reader, poolBase, AsbParameterType.Bool);
                FinishBody(body, reader, version, x2c);
                break;
            }
            case "BoneBlender": {
                var nameNode = ReadParameter(reader, poolBase, AsbParameterType.String);
                if (nameNode is JsonValue value && value.TryGetValue<string>(out var name)) {
                    foreach (var group in bones) {
                        var groupObject = AsbJson.Object(group, "bone group");
                        if (AsbJson.String(groupObject["Name"]) == name) {
                            body["Bone Group"] = groupObject.DeepClone();
                            break;
                        }
                    }
                }

                body["Unknown 1"] = reader.ReadUInt32();
                body["Unknown 2"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                body["Unknown 3"] = reader.ReadUInt32();
                if (version == 0x417) {
                    body["Unknown 4"] = reader.ReadUInt32();
                }

                FinishBody(body, reader, version, x2c);
                break;
            }
            case "State":
            case "SubtractAnimation":
            case "Unknown7":
                FinishBody(body, reader, version, x2c);
                break;
            case "Unknown2":
            case "Unknown4":
                return null;
            default:
                return null;
        }

        return body;
    }

    private static void ReadSimpleSelectorChildren(JsonObject body, BinaryDataReader reader, int poolBase,
        uint version, JsonArray x2c, AsbParameterType type)
    {
        var connections = ReadConnections(reader, version, x2c);
        var children = new JsonArray();
        for (var i = 0; i < connections.ChildOffsets.Count; i++) {
            reader.Seek(connections.ChildOffsets[i]);
            children.Add(new JsonObject {
                [i + 1 == connections.ChildOffsets.Count ? "Default Condition" : "Condition"] =
                    ReadParameter(reader, poolBase, type),
                ["Node Index"] = reader.ReadUInt32()
            });
        }

        AddChildren(body, children);
        MergeConnections(body, connections);
    }

    private static void FinishBody(JsonObject body, BinaryDataReader reader, uint version, JsonArray x2c)
    {
        var connections = ReadConnections(reader, version, x2c);
        var children = new JsonArray();
        foreach (var offset in connections.ChildOffsets) {
            reader.Seek(offset);
            children.Add(reader.ReadUInt32());
        }

        AddChildren(body, children);
        MergeConnections(body, connections);
    }

    private static ConnectionData ReadConnections(BinaryDataReader reader, uint version, JsonArray x2c)
    {
        var counts = new byte[6];
        for (var i = 0; i < counts.Length; i++) {
            counts[i] = reader.ReadByte();
            reader.ReadByte();
        }

        var offsets = new List<uint>[6];
        for (var i = 0; i < offsets.Length; i++) {
            offsets[i] = new List<uint>(counts[i]);
            for (var j = 0; j < counts[i]; j++) {
                offsets[i].Add(reader.ReadUInt32());
            }
        }

        var fields = new JsonObject();
        AddConnectionList(fields, "State Nodes", ReadNodeIndices(reader, offsets[0]));
        var resolvedX2c = new JsonArray();
        foreach (var offset in offsets[3]) {
            reader.Seek(offset);
            if (version == 0x417) {
                var index = reader.ReadInt32();
                var entry = index >= 0
                    ? index < x2c.Count
                        ? x2c[index]!.DeepClone()
                        : throw new InvalidDataException("ASB 0x2C index exceeds table.")
                    : new JsonObject();
                resolvedX2c.Add(new JsonObject {
                    ["0x2C Entry"] = entry,
                    ["Node Index"] = reader.ReadUInt32()
                });
            }
            else {
                resolvedX2c.Add(reader.ReadUInt32());
            }
        }

        AddConnectionList(fields, "0x2C Connections", resolvedX2c);
        AddConnectionList(fields, "Event Node Connections", ReadNodeIndices(reader, offsets[4]));
        AddConnectionList(fields, "Frame Node Connections", ReadNodeIndices(reader, offsets[5]));
        return new ConnectionData(offsets[2], fields);
    }

    private static JsonArray ReadNodeIndices(BinaryDataReader reader, List<uint> offsets)
    {
        var result = new JsonArray();
        foreach (var offset in offsets) {
            reader.Seek(offset);
            result.Add(reader.ReadUInt32());
        }

        return result;
    }

    private static JsonArray ReadTagsPreserving(BinaryDataReader reader, int poolBase, uint offset)
    {
        var returnPosition = reader.Position;
        var result = ReadTags(reader, poolBase, offset);
        reader.Position = returnPosition;
        return result;
    }

    private static void AddChildren(JsonObject body, JsonArray children)
    {
        if (children.Count > 0) {
            body["Child Nodes"] = children;
        }
    }

    private static void AddConnectionList(JsonObject body, string name, JsonArray values)
    {
        if (values.Count > 0) {
            body[name] = values;
        }
    }

    private static void MergeConnections(JsonObject body, ConnectionData connections)
    {
        foreach (var (key, value) in connections.Fields) {
            body[key] = value?.DeepClone();
        }
    }
}
