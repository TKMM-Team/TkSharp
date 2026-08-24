using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    private sealed partial class AsbWriter
    {
        private void WriteBody(BinaryDataWriter writer, string kind, JsonObject body,
            List<(int Position, JsonArray Tags)> tagPatches)
        {
            switch (kind) {
                case "FloatSelector":
                case "StringSelector":
                case "IntSelector":
                case "BoolSelector":
                    WriteParameter(writer, body.Required("Parameter"));
                    WriteParameter(writer, body.Required("Unknown 1"));
                    writer.Write(AsbJson.Bool(body.Required("Unknown 2")) ? 1u : 0u);
                    break;
                case "SkeletalAnimation":
                    WriteParameter(writer, body.Required("Animation"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 1")));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 2")));
                    WriteParameter(writer, body.Required("Unknown 3"));
                    WriteParameter(writer, body.Required("Unknown 4"));
                    break;
                case "State":
                case "Unknown2":
                case "Unknown4":
                case "SubtractAnimation":
                case "Unknown7":
                    break;
                case "OneDimensionalBlender":
                    WriteParameter(writer, body.Required("Parameter"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown")));
                    break;
                case "Sequential":
                    WriteParameter(writer, body.Required("Unknown 1"));
                    WriteParameter(writer, body.Required("Unknown 2"));
                    WriteParameter(writer, body.Required("Unknown 3"));
                    break;
                case "Simultaneous":
                    writer.Write(AsbJson.UInt32(body.Required("Unknown")));
                    break;
                case "Event": {
                    var eventNode = body.Required("Event");
                    var index = AsbJson.IndexOfDeep(_events, eventNode);
                    if (index < 0) {
                        throw new InvalidDataException("ASB event table entry missing.");
                    }

                    writer.Write(checked((uint)index));
                    break;
                }
                case "MaterialAnimation":
                    if (_version == 0x417) {
                        writer.Write(AsbJson.UInt32(body.Required("Unknown 1")));
                    }

                    WriteParameter(writer, body.Required("Animation"));
                    WriteParameter(writer, body.Required("Unknown 2"));
                    break;
                case "FrameController":
                    foreach (var key in new[] { "Animation Rate", "Start Frame", "End Frame" }) {
                        WriteParameter(writer, body.Required(key));
                    }

                    writer.Write(AsbJson.UInt32(body.Required("Unknown Flag")));
                    foreach (var key in new[] {
                                 "Loop Cancel Flag", "Unknown 2", "Unknown 3", "Unknown 4",
                                 "Unknown 5", "Unknown 6", "Unknown 7", "Unknown 8"
                             }) {
                        WriteParameter(writer, body.Required(key));
                    }

                    writer.Write(AsbJson.Bool(body.Required("Unknown 9")) ? 1u : 0u);
                    WriteParameter(writer, body.Required("Unknown 10"));
                    if (_version == 0x417) {
                        WriteParameter(writer, body.Required("Unknown 11"));
                    }

                    writer.Write(AsbJson.UInt32(body.Required("Unknown 12")));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 13")));
                    break;
                case "DummyAnimation":
                    WriteParameter(writer, body.Required("Frame"));
                    WriteParameter(writer, body.Required("Unknown"));
                    break;
                case "RandomSelector":
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 1")));
                    WriteParameter(writer, body.Required("Unknown 2"));
                    WriteParameter(writer, body.Required("Unknown 3"));
                    writer.Write(AsbJson.Bool(body.Required("Unknown 4")) ? 1u : 0u);
                    break;
                case "PreviousTagSelector":
                    writer.Write(AsbJson.UInt32(body.Required("Unknown")));
                    break;
                case "BonePositionSelector":
                    WriteParameter(writer, body.Required("Bone 1"));
                    WriteParameter(writer, body.Required("Bone 2"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 1")));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 2")));
                    WriteParameter(writer, body.Required("Unknown 3"));
                    break;
                case "BoneAnimation":
                    WriteParameter(writer, body.Required("Animation"));
                    WriteParameter(writer, body.Required("Unknown 1"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 2")));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 3")));
                    WriteParameter(writer, body.Required("Unknown 4"));
                    break;
                case "InitialFrame":
                    writer.Write(AsbJson.UInt32(body.Required("Flag")));
                    WriteTagReference(writer, body["Tags"] as JsonArray, tagPatches);
                    if (_version == 0x417) {
                        WriteParameter(writer, body.Required("Unknown 1"));
                    }

                    WriteParameter(writer, body.Required("Bone 1"));
                    WriteParameter(writer, body.Required("Bone 2"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 2")));
                    WriteParameter(writer, body.Required("Unknown 3"));
                    WriteParameter(writer, body.Required("Unknown 4"));
                    break;
                case "BoneBlender": {
                    var group = AsbJson.Object(body.Required("Bone Group"), "bone group");
                    WriteParameter(writer, JsonValue.Create(AsbJson.String(group.Required("Name"))));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 1")));
                    WriteParameter(writer, body.Required("Unknown 2"));
                    writer.Write(AsbJson.UInt32(body.Required("Unknown 3")));
                    if (_version == 0x417) {
                        writer.Write(AsbJson.UInt32(body.Required("Unknown 4")));
                    }

                    break;
                }
                case "Alert":
                    WriteParameter(writer, body.Required("Message"));
                    break;
                case "ShapeAnimation":
                    WriteParameter(writer, body.Required("Animation"));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported ASB node body {kind}.");
            }

            if (kind is not ("Unknown2" or "Unknown4")) {
                WriteConnections(writer, body, kind, tagPatches);
            }
        }

        private void WriteConnections(BinaryDataWriter writer, JsonObject body, string kind,
            List<(int Position, JsonArray Tags)> tagPatches)
        {
            string[] keys = [
                "State Nodes", "Unknown Connection", "Child Nodes", "0x2C Connections",
                "Event Node Connections", "Frame Node Connections"
            ];
            var lists = keys.Select(key => body[key] as JsonArray ?? []).ToArray();
            byte baseIndex = 0;
            foreach (var list in lists) {
                writer.Write(checked((byte)list.Count));
                writer.Write(baseIndex);
                baseIndex = unchecked((byte)(baseIndex + list.Count));
            }

            var patches = lists.Select(list => ReserveUInt32(writer, list.Count)).ToArray();
            for (var groupIndex = 0; groupIndex < lists.Length; groupIndex++) {
                for (var entryIndex = 0; entryIndex < lists[groupIndex].Count; entryIndex++) {
                    writer.PatchUInt32(patches[groupIndex][entryIndex], checked((uint)writer.Position));
                    var value = lists[groupIndex][entryIndex];
                    switch (groupIndex) {
                        case 0:
                        case 4:
                        case 5:
                            writer.Write(AsbJson.UInt32(value));
                            break;
                        case 2:
                            WriteChild(writer, kind, value, tagPatches);
                            break;
                        case 3:
                            if (_version == 0x417) {
                                var connection = AsbJson.Object(value, "0x2C connection");
                                var x2cEntry = AsbJson.Object(connection.Required("0x2C Entry"), "0x2C entry");
                                if (x2cEntry.Count == 0) {
                                    writer.Write(-1);
                                }
                                else {
                                    var index = AsbJson.IndexOfDeep(_x2c, x2cEntry);
                                    if (index < 0) {
                                        throw new InvalidDataException("ASB 0x2C table entry missing.");
                                    }

                                    writer.Write(checked((uint)index));
                                }

                                writer.Write(AsbJson.UInt32(connection.Required("Node Index")));
                            }
                            else {
                                writer.Write(AsbJson.UInt32(value));
                            }

                            break;
                        default:
                            writer.Write(AsbJson.UInt32(value));
                            break;
                    }
                }
            }
        }

        private void WriteChild(BinaryDataWriter writer, string kind, JsonNode? value,
            List<(int Position, JsonArray Tags)> tagPatches)
        {
            if (value is JsonValue) {
                writer.Write(AsbJson.UInt32(value));
                return;
            }

            var child = AsbJson.Object(value, "child node");
            switch (kind) {
                case "FloatSelector":
                case "BonePositionSelector":
                case "OneDimensionalBlender":
                    if (child["Default Condition"] is JsonNode defaultCondition) {
                        WriteParameter(writer, defaultCondition);
                        writer.Write(0ul);
                        writer.Write(AsbJson.UInt32(child.Required("Node Index")));
                    }
                    else {
                        WriteParameter(writer, child.Required("Condition Min"));
                        WriteParameter(writer, child.Required("Condition Max"));
                        writer.Write(AsbJson.UInt32(child.Required("Node Index")));
                    }

                    break;
                case "RandomSelector":
                    WriteParameter(writer, child.Required("Weight"));
                    writer.Write(AsbJson.UInt32(child.Required("Node Index")));
                    break;
                case "IntSelector":
                case "StringSelector":
                    WriteParameter(writer, child["Condition"] ?? child["Default Condition"]
                        ?? throw new InvalidDataException("ASB selector condition missing."));
                    writer.Write(AsbJson.UInt32(child.Required("Node Index")));
                    break;
                case "PreviousTagSelector": {
                    var tags = child["Tags"] as JsonArray ?? [];
                    if (tags.Count == 0) {
                        writer.Write(-1);
                    }
                    else {
                        WriteTagReference(writer, tags, tagPatches);
                    }

                    writer.Write(AsbJson.UInt32(child.Required("Node Index")));
                    break;
                }
                case "BoolSelector":
                    writer.Write(AsbJson.UInt32(child["Condition True"] ?? child["Condition False"]
                        ?? throw new InvalidDataException("ASB bool selector child missing.")));
                    break;
                default:
                    writer.Write(AsbJson.UInt32(value));
                    break;
            }
        }
    }
}
