using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    private static readonly string[] BlackboardTypes = ["string", "int", "float", "bool", "vec3f", "userdefined"];

    private static JsonObject ReadBlackboard(BinaryDataReader reader, int poolBase)
    {
        var headers = new List<(ushort Count, ushort Offset)>();
        foreach (var _ in BlackboardTypes) {
            var count = reader.ReadUInt16();
            reader.ReadUInt16();
            var offset = reader.ReadUInt16();
            reader.ReadUInt16();
            headers.Add((count, offset));
        }

        var groups = new List<JsonArray>();
        var referenceIndices = new List<List<int?>>();
        var maxReference = -1;
        foreach (var header in headers) {
            var group = new JsonArray();
            var indices = new List<int?>();
            for (var i = 0; i < header.Count; i++) {
                var bits = reader.ReadUInt32();
                int? reference = null;
                if ((bits >> 31) != 0) {
                    reference = (int)((bits >> 24) & 0x7f);
                    maxReference = Math.Max(maxReference, reference.Value);
                }

                group.Add(new JsonObject {
                    ["Name"] = reader.ReadCStringAt(poolBase + (bits & 0x3fffff))
                });
                indices.Add(reference);
            }

            groups.Add(group);
            referenceIndices.Add(indices);
        }

        var valuesBase = reader.Position;
        for (var kindIndex = 0; kindIndex < BlackboardTypes.Length; kindIndex++) {
            reader.Seek(valuesBase + headers[kindIndex].Offset);
            foreach (var node in groups[kindIndex]) {
                AsbJson.Object(node, "blackboard parameter")["Init Value"] =
                    ReadBlackboardInitial(reader, poolBase, BlackboardTypes[kindIndex]);
            }
        }

        var references = new List<string>();
        for (var i = 0; i <= maxReference; i++) {
            references.Add(reader.ReadCStringAt(poolBase + reader.ReadUInt32()));
            reader.Skip(12);
        }

        var result = new JsonObject();
        for (var kindIndex = 0; kindIndex < BlackboardTypes.Length; kindIndex++) {
            var group = groups[kindIndex];
            for (var entryIndex = 0; entryIndex < group.Count; entryIndex++) {
                if (referenceIndices[kindIndex][entryIndex] is not { } reference) {
                    continue;
                }

                if (reference >= references.Count) {
                    throw new InvalidDataException("ASB blackboard reference index exceeds table.");
                }

                AsbJson.Object(group[entryIndex], "blackboard parameter")["File Reference"] =
                    new JsonObject { ["Filename"] = references[reference] };
            }

            if (group.Count > 0) {
                result[BlackboardTypes[kindIndex]] = group;
            }
        }

        return result;
    }

    private static JsonNode? ReadBlackboardInitial(BinaryDataReader reader, int poolBase, string kind)
    {
        return kind switch {
            "string" => JsonValue.Create(reader.ReadCStringAt(poolBase + reader.ReadUInt32())),
            "int" => JsonValue.Create(reader.ReadUInt32()),
            "float" => JsonValue.Create(reader.ReadSingle()),
            "bool" => JsonValue.Create(reader.ReadUInt32() != 0),
            "vec3f" => new JsonArray(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            "userdefined" => null,
            _ => throw new InvalidDataException($"Invalid blackboard type {kind}.")
        };
    }

    private static JsonArray ReadCommandGroups(BinaryDataReader reader, int poolBase, uint offset)
    {
        var result = new JsonArray();
        if (offset == 0) {
            return result;
        }

        reader.Seek(offset);
        var count = reader.ReadUInt32();
        for (var i = 0u; i < count; i++) {
            var valuesOffset = reader.ReadUInt32();
            var returnPosition = reader.Position;
            result.Add(ReadTags(reader, poolBase, valuesOffset));
            reader.Position = returnPosition;
        }

        return result;
    }

    private static JsonArray ReadTransitions(BinaryDataReader reader, int poolBase, uint offset, JsonArray groups)
    {
        reader.Seek(offset);
        var count = reader.ReadUInt32();
        reader.ReadUInt32();
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var entryCount = reader.ReadUInt32();
            var unknown = reader.ReadInt32();
            var entriesOffset = reader.ReadUInt32();
            var returnPosition = reader.Position;
            reader.Seek(entriesOffset);
            var entries = new JsonArray();
            for (var j = 0u; j < entryCount; j++) {
                entries.Add(ReadTransitionEntry(reader, poolBase, groups));
            }

            reader.Position = returnPosition;
            result.Add(new JsonObject {
                ["Unknown"] = unknown,
                ["Transitions"] = entries
            });
        }

        return result;
    }

    private static JsonObject ReadTransitionEntry(BinaryDataReader reader, int poolBase, JsonArray groups)
    {
        var command1 = reader.ReadCStringAt(poolBase + reader.ReadUInt32());
        var command2 = reader.ReadCStringAt(poolBase + reader.ReadUInt32());
        var (typeName, type) = reader.ReadByte() switch {
            0 => ("int", AsbParameterType.Int),
            1 => ("string", AsbParameterType.String),
            2 => ("float", AsbParameterType.Float),
            3 => ("bool", AsbParameterType.Bool),
            _ => ("vec3f", AsbParameterType.Vec3)
        };
        var allowMultiple = reader.ReadByte() != 0;
        var groupIndex = (int)reader.ReadUInt16() - 1;
        var parameter = reader.ReadCStringAt(poolBase + reader.ReadUInt32());
        var value = ReadParameter(reader, poolBase, type);
        if (type != AsbParameterType.Vec3) {
            reader.Skip(8);
        }

        var result = new JsonObject {
            ["Command 1"] = command1,
            ["Command 2"] = command2,
            ["Parameter Type"] = typeName,
            ["Allow Multiple Matches"] = allowMultiple,
            ["Parameter"] = parameter,
            ["Value"] = value
        };
        if (groupIndex >= 0) {
            if (groupIndex >= groups.Count) {
                throw new InvalidDataException("ASB command group index exceeds table.");
            }

            result["Command Group"] = groups[groupIndex]!.DeepClone();
        }

        return result;
    }

    private static JsonObject ReadAnimationSlot(BinaryDataReader reader, int poolBase)
    {
        var count = reader.ReadUInt16();
        var result = new JsonObject {
            ["Unknown"] = reader.ReadUInt16(),
            ["Partial 1"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
            ["Partial 2"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32())
        };
        var entries = new JsonArray();
        for (var i = 0; i < count; i++) {
            entries.Add(new JsonObject {
                ["Bone"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
                ["Unknown 1"] = reader.ReadUInt16(),
                ["Unknown 2"] = reader.ReadUInt16()
            });
        }

        result["Entries"] = entries;
        return result;
    }

    private static JsonArray ReadEvents(BinaryDataReader reader, int poolBase, uint offset, uint count)
    {
        reader.Seek(offset);
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var eventOffset = reader.ReadUInt32();
            var returnPosition = reader.Position;
            reader.Seek(eventOffset);
            var triggerCount = reader.ReadUInt32();
            var holdCount = reader.ReadUInt32();
            var triggers = new JsonArray();
            var holds = new JsonArray();
            for (var j = 0u; j < triggerCount; j++) {
                triggers.Add(ReadAsbEventEntry(reader, poolBase, false));
            }

            for (var j = 0u; j < holdCount; j++) {
                holds.Add(ReadAsbEventEntry(reader, poolBase, true));
            }

            result.Add(new JsonObject {
                ["Trigger Events"] = triggers,
                ["Hold Events"] = holds
            });
            reader.Position = returnPosition;
        }

        return result;
    }

    private static JsonObject ReadAsbEventEntry(BinaryDataReader reader, int poolBase, bool hold)
    {
        var result = new JsonObject {
            ["Name"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
            ["Unknown 1"] = reader.ReadUInt32()
        };
        var parametersOffset = reader.ReadUInt32();
        reader.ReadUInt32();
        result["Unknown Hash"] = AsbJson.Hex(reader.ReadUInt32());
        result["Start Frame"] = reader.ReadSingle();
        if (hold) {
            result["End Frame"] = reader.ReadSingle();
        }

        var returnPosition = reader.Position;
        reader.Seek(parametersOffset);
        result["Parameters"] = ReadAsbEventParameters(reader, poolBase);
        reader.Position = returnPosition;
        return result;
    }

    private static JsonArray ReadAsbEventParameters(BinaryDataReader reader, int poolBase)
    {
        var count = reader.ReadUInt32();
        var offsets = new uint[count];
        for (var i = 0; i < count; i++) {
            offsets[i] = reader.ReadUInt32();
        }

        var result = new JsonArray();
        foreach (var tagged in offsets) {
            var type = (tagged >> 24) switch {
                0x40 => AsbParameterType.String,
                0x30 => AsbParameterType.Float,
                0x20 => AsbParameterType.Int,
                0x10 => AsbParameterType.Bool,
                var value => throw new InvalidDataException($"Invalid event parameter tag 0x{value:x}.")
            };
            reader.Seek(tagged & 0xffffff);
            result.Add(ReadParameter(reader, poolBase, type));
        }

        return result;
    }

    private static JsonArray ReadX2c(BinaryDataReader reader, int poolBase, uint offset)
    {
        reader.Seek(offset);
        var count = reader.ReadUInt32();
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var entry = new JsonObject {
                ["Source Node"] = reader.ReadUInt16(),
                ["Target Node"] = reader.ReadUInt16(),
                ["Unknown 1"] = reader.ReadUInt32(),
                ["Unknown 2"] = reader.ReadUInt32(),
                ["Unknown 3"] = reader.ReadUInt32()
            };
            var subEntries = new JsonArray();
            for (var j = 0; j < 4; j++) {
                var entryType = reader.ReadUInt16();
                var sub = new JsonObject {
                    ["Entry Type"] = entryType,
                    ["Unknown Type"] = reader.ReadUInt16()
                };
                if (entryType == 0) {
                    reader.Skip(16);
                }
                else {
                    var type = entryType switch {
                        1 => AsbParameterType.Float,
                        2 => AsbParameterType.Int,
                        3 => AsbParameterType.Bool,
                        _ => AsbParameterType.String
                    };
                    sub["Unknown 1"] = ReadParameter(reader, poolBase, type);
                    sub["Unknown 2"] = ReadParameter(reader, poolBase, type);
                }

                subEntries.Add(sub);
            }

            entry["Entries"] = subEntries;
            result.Add(entry);
        }

        return result;
    }

    private static JsonArray ReadTags(BinaryDataReader reader, int poolBase, uint offset)
    {
        reader.Seek(offset);
        var count = reader.ReadUInt32();
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            result.Add(reader.ReadCStringAt(poolBase + reader.ReadUInt32()));
        }

        return result;
    }

    private static JsonArray ReadX68(BinaryDataReader reader, int poolBase, uint? offset)
    {
        var result = new JsonArray();
        if (offset is null) {
            return result;
        }

        reader.Seek(offset.Value);
        var count = reader.ReadUInt32();
        for (var i = 0u; i < count; i++) {
            result.Add(new JsonObject {
                ["Name"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
                ["Unknown"] = reader.ReadSingle()
            });
        }

        return result;
    }

    private static JsonArray ReadX38(BinaryDataReader reader, int poolBase, uint offset, uint count)
    {
        reader.Seek(offset);
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var type = reader.ReadUInt32();
            var valueOffset = reader.ReadUInt32();
            var guid = ReadGuid(reader);
            var returnPosition = reader.Position;
            reader.Seek(valueOffset);
            var entry = new JsonObject();
            if (type == 0) {
                entry["Start Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                entry["Unknown 2"] = reader.ReadUInt32();
            }
            else if (type == 1) {
                entry["Start Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                entry["End Frame"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
                entry["Unknown 3"] = ReadParameter(reader, poolBase, AsbParameterType.Float);
            }
            else if (type != 3) {
                throw new InvalidDataException($"Invalid ASB 0x38 type {type}.");
            }

            reader.Position = returnPosition;
            result.Add(new JsonObject {
                ["Type"] = type,
                ["GUID"] = guid,
                ["Entry"] = entry
            });
        }

        return result;
    }

    private static JsonArray ReadX40(BinaryDataReader reader, uint offset, uint count, uint version)
    {
        reader.Seek(offset);
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var entry = new JsonObject {
                ["Unknown 1"] = reader.ReadUInt32(),
                ["Angle"] = reader.ReadSingle()
            };
            if (version == 0x417) {
                entry["Type"] = reader.ReadUInt32();
            }

            entry["Unknown 2"] = reader.ReadSingle();
            entry["Rate"] = reader.ReadSingle();
            entry["Unknown 3"] = reader.ReadSingle();
            entry["Min"] = reader.ReadSingle();
            entry["Max"] = reader.ReadSingle();
            result.Add(entry);
        }

        return result;
    }

    private static JsonArray ReadBones(BinaryDataReader reader, int poolBase, uint offset, uint count)
    {
        reader.Seek(offset);
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            var bonesOffset = reader.ReadUInt32();
            var name = reader.ReadCStringAt(poolBase + reader.ReadUInt32());
            var boneCount = reader.ReadUInt32();
            var unknown = reader.ReadUInt32();
            var returnPosition = reader.Position;
            reader.Seek(bonesOffset);
            var bones = new JsonArray();
            for (var j = 0u; j < boneCount; j++) {
                bones.Add(new JsonObject {
                    ["Name"] = reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
                    ["Unknown"] = reader.ReadSingle()
                });
            }

            reader.Position = returnPosition;
            result.Add(new JsonObject {
                ["Name"] = name,
                ["Unknown"] = unknown,
                ["Bones"] = bones
            });
        }

        return result;
    }

    private static JsonArray ReadMarkings(BinaryDataReader reader, int poolBase, uint offset)
    {
        reader.Seek(offset);
        var count = reader.ReadUInt32();
        var result = new JsonArray();
        for (var i = 0u; i < count; i++) {
            result.Add(new JsonArray(
                reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
                reader.ReadCStringAt(poolBase + reader.ReadUInt32()),
                reader.ReadCStringAt(poolBase + reader.ReadUInt32())));
        }

        return result;
    }
}
