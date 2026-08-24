using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal static partial class AsbCodec
{
    private sealed partial class AsbWriter
    {
        private void WriteEvent(BinaryDataWriter writer, JsonObject asbEvent)
        {
            var triggers = AsbJson.Array(asbEvent.Required("Trigger Events"), "trigger events");
            var holds = AsbJson.Array(asbEvent.Required("Hold Events"), "hold events");
            writer.Write(checked((uint)triggers.Count));
            writer.Write(checked((uint)holds.Count));
            var parameterPatches = new List<int>();

            foreach (var entryNode in triggers) {
                var entry = AsbJson.Object(entryNode, "trigger event");
                writer.Write(_strings.Offset(AsbJson.String(entry.Required("Name"))));
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 1")));
                parameterPatches.Add(writer.ReserveUInt32());
                var parameters = AsbJson.Array(entry.Required("Parameters"), "event parameters");
                writer.Write(checked((uint)(parameters.Count * 8)));
                writer.Write(AsbJson.ParseHex(AsbJson.String(entry.Required("Unknown Hash"))));
                writer.Write(AsbJson.Single(entry.Required("Start Frame")));
            }

            foreach (var entryNode in holds) {
                var entry = AsbJson.Object(entryNode, "hold event");
                writer.Write(_strings.Offset(AsbJson.String(entry.Required("Name"))));
                writer.Write(AsbJson.UInt32(entry.Required("Unknown 1")));
                parameterPatches.Add(writer.ReserveUInt32());
                var parameters = AsbJson.Array(entry.Required("Parameters"), "event parameters");
                writer.Write(checked((uint)(parameters.Count * 8)));
                writer.Write(AsbJson.ParseHex(AsbJson.String(entry.Required("Unknown Hash"))));
                writer.Write(AsbJson.Single(entry.Required("Start Frame")));
                writer.Write(AsbJson.Single(entry.Required("End Frame")));
            }

            var parameterLists = triggers.Concat(holds)
                .Select(x => AsbJson.Array(AsbJson.Object(x, "event entry").Required("Parameters"), "event parameters"))
                .ToArray();
            var valuePatches = new List<(int Position, uint Flag, JsonNode? Value)>();
            for (var i = 0; i < parameterLists.Length; i++) {
                writer.PatchUInt32(parameterPatches[i], checked((uint)writer.Position));
                writer.Write(checked((uint)parameterLists[i].Count));
                foreach (var parameter in parameterLists[i]) {
                    valuePatches.Add((writer.ReserveUInt32(), EventParameterFlag(parameter), parameter));
                }
            }

            foreach (var (position, flag, value) in valuePatches) {
                writer.PatchUInt32(position, checked((uint)writer.Position) | (flag << 24));
                WriteParameter(writer, value);
            }
        }

        private static uint EventParameterFlag(JsonNode? value)
        {
            if (value is JsonObject reference) {
                if (reference["Type"] is JsonNode type) {
                    return AsbJson.String(type) switch {
                        "bool" => 0x10,
                        "int" => 0x20,
                        "float" => 0x30,
                        "string" => 0x40,
                        _ => throw new InvalidDataException("Invalid ASB event parameter type.")
                    };
                }

                if (reference["Default Value"] is JsonNode defaultValue) {
                    return EventParameterFlag(defaultValue);
                }

                throw new InvalidDataException("ASB event parameter reference has no type.");
            }

            if (value is not JsonValue json) {
                throw new InvalidDataException("Invalid ASB event parameter.");
            }

            if (json.TryGetValue<bool>(out _)) return 0x10;
            if (json.TryGetValue<int>(out _) || json.TryGetValue<uint>(out _) || json.TryGetValue<long>(out _)) return 0x20;
            if (json.TryGetValue<float>(out _) || json.TryGetValue<double>(out _)) return 0x30;
            if (json.TryGetValue<string>(out _)) return 0x40;
            throw new InvalidDataException("Invalid ASB event parameter.");
        }

        private (uint TransitionsOffset, uint GroupsOffset) WriteTransitions(BinaryDataWriter writer)
        {
            var start = checked((uint)writer.Position);
            writer.Write(checked((uint)_document.Transitions.Count));
            writer.Write(0u);
            var entryPatches = new List<int>();
            foreach (var transitionNode in _document.Transitions) {
                var transition = AsbJson.Object(transitionNode, "transition");
                var entries = AsbJson.Array(transition.Required("Transitions"), "transition entries");
                writer.Write(checked((uint)entries.Count));
                writer.Write(AsbJson.Int32(transition.Required("Unknown")));
                entryPatches.Add(writer.ReserveUInt32());
            }

            var groups = new JsonArray();
            for (var i = 0; i < _document.Transitions.Count; i++) {
                writer.PatchUInt32(entryPatches[i], checked((uint)writer.Position));
                var transition = AsbJson.Object(_document.Transitions[i], "transition");
                foreach (var entryNode in AsbJson.Array(transition.Required("Transitions"), "transition entries")) {
                    var entry = AsbJson.Object(entryNode, "transition entry");
                    writer.Write(_strings.Offset(AsbJson.String(entry.Required("Command 1"))));
                    writer.Write(_strings.Offset(AsbJson.String(entry.Required("Command 2"))));
                    writer.Write(AsbJson.String(entry.Required("Parameter Type")) switch {
                        "int" => (byte)0,
                        "string" => (byte)1,
                        "float" => (byte)2,
                        "bool" => (byte)3,
                        "vec3f" => (byte)4,
                        var type => throw new InvalidDataException($"Invalid transition parameter type {type}.")
                    });
                    writer.Write(AsbJson.Bool(entry.Required("Allow Multiple Matches")) ? (byte)1 : (byte)0);
                    ushort groupIndex = 0;
                    if (entry["Command Group"] is JsonArray group) {
                        AsbJson.AddUnique(groups, group);
                        groupIndex = checked((ushort)(AsbJson.IndexOfDeep(groups, group) + 1));
                    }

                    writer.Write(groupIndex);
                    writer.Write(_strings.Offset(AsbJson.String(entry.Required("Parameter"))));
                    WriteParameter(writer, entry.Required("Value"));
                    if (AsbJson.String(entry.Required("Parameter Type")) != "vec3f") {
                        writer.Write(0ul);
                    }
                }
            }

            if (groups.Count == 0) {
                return (start, 0);
            }

            var groupsOffset = checked((uint)writer.Position);
            writer.Write(checked((uint)groups.Count));
            var groupPatches = ReserveUInt32(writer, groups.Count);
            for (var i = 0; i < groups.Count; i++) {
                writer.PatchUInt32(groupPatches[i], checked((uint)writer.Position));
                var group = AsbJson.Array(groups[i], "command group");
                writer.Write(checked((uint)group.Count));
                foreach (var value in group) {
                    writer.Write(_strings.Offset(AsbJson.String(value)));
                }
            }

            return (start, groupsOffset);
        }

        private uint WriteBlackboard(BinaryDataWriter writer)
        {
            var start = checked((uint)writer.Position);
            ushort index = 0;
            ushort offset = 0;
            foreach (var kind in BlackboardTypes) {
                var group = _document.LocalBlackboard[kind] as JsonArray;
                var count = checked((ushort)(group?.Count ?? 0));
                writer.Write(count);
                writer.Write(index);
                index = checked((ushort)(index + count));
                writer.Write(offset);
                offset = checked((ushort)(offset + count * (kind == "vec3f" ? 12 : 4)));
                writer.Write((ushort)0);
            }

            var references = new List<string>();
            foreach (var kind in BlackboardTypes) {
                if (_document.LocalBlackboard[kind] is not JsonArray group) {
                    continue;
                }

                foreach (var entryNode in group) {
                    var entry = AsbJson.Object(entryNode, "blackboard parameter");
                    var nameOffset = _strings.Offset(AsbJson.String(entry.Required("Name")));
                    if (entry["File Reference"] is JsonObject reference) {
                        var filename = AsbJson.String(reference.Required("Filename"));
                        var referenceIndex = references.IndexOf(filename);
                        if (referenceIndex < 0) {
                            referenceIndex = references.Count;
                            references.Add(filename);
                        }

                        nameOffset |= 0x80000000u | (checked((uint)referenceIndex) << 24);
                    }

                    writer.Write(nameOffset);
                }
            }

            foreach (var kind in BlackboardTypes) {
                if (_document.LocalBlackboard[kind] is not JsonArray group) {
                    continue;
                }

                foreach (var entryNode in group) {
                    var entry = AsbJson.Object(entryNode, "blackboard parameter");
                    var initial = entry["Init Value"];
                    switch (kind) {
                        case "string":
                            writer.Write(_strings.Offset(initial is null ? "" : AsbJson.String(initial)));
                            break;
                        case "int":
                            writer.Write(AsbJson.UInt32(initial));
                            break;
                        case "float":
                            writer.Write(AsbJson.Single(initial));
                            break;
                        case "bool":
                            writer.Write(AsbJson.Bool(initial) ? 1u : 0u);
                            break;
                        case "vec3f":
                            foreach (var component in AsbJson.Array(initial, "blackboard vec3")) {
                                writer.Write(AsbJson.Single(component));
                            }

                            break;
                    }
                }
            }

            foreach (var filename in references) {
                writer.Write(_strings.Offset(filename));
                writer.Write(new byte[12]);
            }

            return start;
        }

        private uint WriteSlots(BinaryDataWriter writer)
        {
            var start = checked((uint)writer.Position);
            foreach (var slotNode in _document.AnimationSlots) {
                var slot = AsbJson.Object(slotNode, "animation slot");
                var entries = AsbJson.Array(slot.Required("Entries"), "animation slot entries");
                writer.Write(checked((ushort)entries.Count));
                writer.Write(checked((ushort)AsbJson.UInt32(slot.Required("Unknown"))));
                writer.Write(_strings.Offset(AsbJson.String(slot.Required("Partial 1"))));
                writer.Write(_strings.Offset(AsbJson.String(slot.Required("Partial 2"))));
                foreach (var entryNode in entries) {
                    var entry = AsbJson.Object(entryNode, "animation slot entry");
                    writer.Write(_strings.Offset(AsbJson.String(entry.Required("Bone"))));
                    writer.Write(checked((ushort)AsbJson.UInt32(entry.Required("Unknown 1"))));
                    writer.Write(checked((ushort)AsbJson.UInt32(entry.Required("Unknown 2"))));
                }
            }

            return start;
        }

        private uint WriteBones(BinaryDataWriter writer)
        {
            var start = checked((uint)writer.Position);
            var patches = new List<int>();
            foreach (var groupNode in _bones) {
                var group = AsbJson.Object(groupNode, "bone group");
                patches.Add(writer.ReserveUInt32());
                writer.Write(_strings.Offset(AsbJson.String(group.Required("Name"))));
                writer.Write(checked((uint)AsbJson.Array(group.Required("Bones"), "bones").Count));
                writer.Write(AsbJson.UInt32(group.Required("Unknown")));
            }

            for (var i = 0; i < _bones.Count; i++) {
                writer.PatchUInt32(patches[i], checked((uint)writer.Position));
                var group = AsbJson.Object(_bones[i], "bone group");
                foreach (var boneNode in AsbJson.Array(group.Required("Bones"), "bones")) {
                    var bone = AsbJson.Object(boneNode, "bone");
                    writer.Write(_strings.Offset(AsbJson.String(bone.Required("Name"))));
                    writer.Write(AsbJson.Single(bone.Required("Unknown")));
                }
            }

            return start;
        }
    }
}
