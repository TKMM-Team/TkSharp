using System.Text.Json.Nodes;

namespace TkSharp.Merging.Mergers.Asb;

internal sealed class AsbMergeReport
{
    public List<string> NodeConflicts { get; } = [];
    public List<string> CommandConflicts { get; } = [];
    public List<string> SectionConflicts { get; } = [];
    public List<string> GuidConflicts { get; } = [];
    public int AppendedNodes { get; set; }
    public int AddedCommands { get; set; }
}

internal sealed class BaevMergeReport
{
    public List<string> NodeConflicts { get; } = [];
    public List<string> EventConflicts { get; } = [];
    public int AddedNodes { get; set; }
    public int AddedEvents { get; set; }
}

internal static class AsbMergeEngine
{
    private static readonly HashSet<string> ReferenceListKeys = [
        "Child Nodes", "State Nodes", "Event Node Connections", "Frame Node Connections", "0x2C Connections"
    ];
    private static readonly HashSet<string> ReferenceValueKeys = [
        "Node Index", "Condition True", "Condition False"
    ];

    private sealed record NormalizedCommand(JsonObject Command, string Left, string? Right);

    public static (AsbDocument Document, AsbMergeReport Report) Merge(
        AsbDocument vanilla, IReadOnlyList<AsbDocument> inputs)
    {
        var vanillaCount = checked((uint)vanilla.Nodes.Count);
        for (var index = 0u; index < vanillaCount; index++) {
            if (!vanilla.Nodes.ContainsKey(index)) {
                throw new InvalidDataException($"Vanilla ASB node indices are not contiguous at {index}.");
            }
        }

        var baseNodes = NormalizeNodes(vanilla, null, vanillaCount);
        var baseCommands = NormalizeCommands(vanilla, null, vanillaCount);
        var baseCommandValues = baseCommands.ToDictionary(x => x.Key, x => CommandValue(x.Value));
        var merged = vanilla.DeepClone();
        var mergedNodes = baseNodes.ToDictionary();
        var mergedCommands = baseCommands.ToDictionary();
        var commandOrder = vanilla.Commands
            .Select(x => AsbJson.String(AsbJson.Object(x, "command").Required("Name"))).ToList();
        var changedNodes = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var changedCommands = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var changedSections = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var appendedOrder = new List<string>();
        var report = new AsbMergeReport();

        for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++) {
            var input = inputs[inputIndex];
            if (input.Version != vanilla.Version) {
                throw new InvalidDataException(
                    $"ASB input {inputIndex} has version 0x{input.Version:x}, expected 0x{vanilla.Version:x}.");
            }

            var inputNodes = NormalizeNodes(input, inputIndex, vanillaCount);
            var inputCommands = NormalizeCommands(input, inputIndex, vanillaCount);
            var inputCommandValues = inputCommands.ToDictionary(x => x.Key, x => CommandValue(x.Value));

            for (var index = 0u; index < vanillaCount; index++) {
                var stableId = VanillaId(index);
                if (!inputNodes.TryGetValue(stableId, out var inputNode)) {
                    continue;
                }

                var inputValue = NodeValue(inputNode);
                var baseValue = baseNodes.TryGetValue(stableId, out var baseNode)
                    ? NodeValue(baseNode)
                    : null;
                if (JsonNode.DeepEquals(inputValue, baseValue)) {
                    continue;
                }

                if (changedNodes.TryGetValue(stableId, out var previous) &&
                    !JsonNode.DeepEquals(previous, inputValue)) {
                    report.NodeConflicts.Add(stableId);
                }

                changedNodes[stableId] = inputValue;
                mergedNodes[stableId] = inputNode.CloneObject();
            }

            foreach (var (stableId, node) in inputNodes) {
                if (stableId.StartsWith("V:", StringComparison.Ordinal)) {
                    continue;
                }

                appendedOrder.Add(stableId);
                mergedNodes[stableId] = node.CloneObject();
            }

            foreach (var (name, command) in inputCommands) {
                var inputValue = inputCommandValues[name];
                var changed = !baseCommandValues.TryGetValue(name, out var baseValue) ||
                              !JsonNode.DeepEquals(inputValue, baseValue);
                if (!changed) {
                    continue;
                }

                if (changedCommands.TryGetValue(name, out var previous) &&
                    !JsonNode.DeepEquals(previous, inputValue)) {
                    report.CommandConflicts.Add(name);
                }

                if (!mergedCommands.ContainsKey(name)) {
                    commandOrder.Add(name);
                    report.AddedCommands++;
                }

                changedCommands[name] = inputValue;
                mergedCommands[name] = CloneCommand(command);
            }

            MergeSection("Local Blackboard Parameters", vanilla.LocalBlackboard, input.LocalBlackboard,
                value => merged.LocalBlackboard = ((JsonObject)value!).CloneObject(), changedSections, report);
            MergeSection("Transitions", vanilla.Transitions, input.Transitions,
                value => merged.Transitions = ((JsonArray)value!).CloneArray(), changedSections, report);
            MergeSection("Animation Slots", vanilla.AnimationSlots, input.AnimationSlots,
                value => merged.AnimationSlots = ((JsonArray)value!).CloneArray(), changedSections, report);
            MergeSection("Valid Tag List", vanilla.ValidTags, input.ValidTags,
                value => merged.ValidTags = ((JsonArray)value!).CloneArray(), changedSections, report);
            MergeSection("0x68 Section", vanilla.X68, input.X68,
                value => merged.X68 = ((JsonArray)value!).CloneArray(), changedSections, report);
            MergeBytesSection("EXB Section", vanilla.Exb, input.Exb,
                value => merged.Exb = value?.ToArray(), changedSections, report);
        }

        report.AppendedNodes = appendedOrder.Count;
        var stableIndices = BuildFinalIndices(vanillaCount, appendedOrder);
        merged.Nodes = FinalNodes(vanillaCount, appendedOrder, mergedNodes, stableIndices);
        merged.Commands = FinalCommands(commandOrder, mergedCommands, stableIndices);

        var guidOwners = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (index, node) in merged.Nodes) {
            var guid = AsbJson.String(node.Required("GUID"));
            if (guidOwners.TryGetValue(guid, out var previous)) {
                report.GuidConflicts.Add($"{guid} (nodes {previous} and {index})");
            }
            else {
                guidOwners[guid] = index;
            }
        }

        SortDistinct(report.NodeConflicts);
        SortDistinct(report.CommandConflicts);
        SortDistinct(report.SectionConflicts);
        SortDistinct(report.GuidConflicts);
        return (merged, report);
    }

    public static (BaevDocument Document, BaevMergeReport Report) Merge(
        BaevDocument vanilla, IReadOnlyList<BaevDocument> inputs)
    {
        var merged = vanilla.DeepClone();
        var changedUnknowns = new Dictionary<string, uint>(StringComparer.Ordinal);
        var changedEvents = new Dictionary<string, BaevEvent>(StringComparer.Ordinal);
        var report = new BaevMergeReport();

        foreach (var input in inputs) {
            foreach (var (groupHash, inputNodes) in input.Groups) {
                foreach (var inputNode in inputNodes) {
                    var baseNode = vanilla.Groups.TryGetValue(groupHash, out var vanillaNodes)
                        ? vanillaNodes.FirstOrDefault(x => x.Hash == inputNode.Hash)
                        : null;
                    if (!merged.Groups.TryGetValue(groupHash, out var mergedNodes)) {
                        mergedNodes = [];
                        merged.Groups[groupHash] = mergedNodes;
                    }

                    var nodeIndex = mergedNodes.FindIndex(x => x.Hash == inputNode.Hash);
                    if (nodeIndex < 0) {
                        mergedNodes.Add(BaevDocument.CloneNode(inputNode));
                        report.AddedNodes++;
                        report.AddedEvents += inputNode.Events.Count;
                        continue;
                    }

                    var nodeKey = $"{groupHash}/{inputNode.Hash}";
                    if (baseNode is null || baseNode.Unknown != inputNode.Unknown) {
                        if (changedUnknowns.TryGetValue(nodeKey, out var previous) && previous != inputNode.Unknown) {
                            report.NodeConflicts.Add(nodeKey);
                        }

                        changedUnknowns[nodeKey] = inputNode.Unknown;
                        mergedNodes[nodeIndex] = mergedNodes[nodeIndex] with { Unknown = inputNode.Unknown };
                    }

                    foreach (var (eventName, inputEvent) in inputNode.Events) {
                        var unchanged = baseNode is not null &&
                                        baseNode.Events.TryGetValue(eventName, out var baseEvent) &&
                                        EventEquals(baseEvent, inputEvent);
                        if (unchanged) {
                            continue;
                        }

                        var eventKey = $"{nodeKey}/{eventName}";
                        if (changedEvents.TryGetValue(eventKey, out var previous) &&
                            !EventEquals(previous, inputEvent)) {
                            report.EventConflicts.Add(eventKey);
                        }

                        var currentEvents = mergedNodes[nodeIndex].Events;
                        if (!currentEvents.ContainsKey(eventName)) {
                            report.AddedEvents++;
                        }

                        changedEvents[eventKey] = CloneEvent(inputEvent);
                        currentEvents[eventName] = CloneEvent(inputEvent);
                    }
                }
            }
        }

        SortDistinct(report.NodeConflicts);
        SortDistinct(report.EventConflicts);
        return (merged, report);
    }

    private static SortedDictionary<string, JsonObject> NormalizeNodes(
        AsbDocument document, int? inputIndex, uint vanillaCount)
    {
        var result = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (index, source) in document.Nodes) {
            var node = source.CloneObject();
            if (node["Body"] is JsonNode body) {
                NormalizeReferences(body, inputIndex, vanillaCount, null);
            }

            result[StableId(index, inputIndex, vanillaCount)] = node;
        }

        return result;
    }

    private static SortedDictionary<string, NormalizedCommand> NormalizeCommands(
        AsbDocument document, int? inputIndex, uint vanillaCount)
    {
        var result = new SortedDictionary<string, NormalizedCommand>(StringComparer.Ordinal);
        foreach (var commandNode in document.Commands) {
            var command = AsbJson.Object(commandNode, "command");
            var left = StableId(AsbJson.UInt32(command.Required("Left Node Index")), inputIndex, vanillaCount);
            var rightIndex = AsbJson.Int32(command.Required("Right Node Index"));
            var right = rightIndex < 0 ? null : StableId(checked((uint)rightIndex), inputIndex, vanillaCount);
            result[AsbJson.String(command.Required("Name"))] =
                new NormalizedCommand(command.CloneObject(), left, right);
        }

        return result;
    }

    private static void NormalizeReferences(JsonNode value, int? inputIndex, uint vanillaCount, string? parentKey)
    {
        if (value is JsonObject obj) {
            foreach (var key in obj.Select(x => x.Key).ToArray()) {
                var child = obj[key];
                if (child is null) continue;
                if (ReferenceValueKeys.Contains(key) && TryUInt32(child, out var index)) {
                    obj[key] = StableId(index, inputIndex, vanillaCount);
                }
                else {
                    NormalizeReferences(child, inputIndex, vanillaCount, key);
                }
            }
        }
        else if (value is JsonArray array) {
            for (var i = 0; i < array.Count; i++) {
                var child = array[i];
                if (child is null) continue;
                if (parentKey is not null && ReferenceListKeys.Contains(parentKey) &&
                    TryUInt32(child, out var index)) {
                    array[i] = StableId(index, inputIndex, vanillaCount);
                }
                else {
                    NormalizeReferences(child, inputIndex, vanillaCount, parentKey);
                }
            }
        }
    }

    private static void DenormalizeReferences(JsonNode value, IReadOnlyDictionary<string, uint> indices,
        string? parentKey)
    {
        if (value is JsonObject obj) {
            foreach (var key in obj.Select(x => x.Key).ToArray()) {
                var child = obj[key];
                if (child is null) continue;
                if (ReferenceValueKeys.Contains(key) && TryStableId(child, out var stableId)) {
                    obj[key] = ResolveIndex(stableId, indices);
                }
                else {
                    DenormalizeReferences(child, indices, key);
                }
            }
        }
        else if (value is JsonArray array) {
            for (var i = 0; i < array.Count; i++) {
                var child = array[i];
                if (child is null) continue;
                if (parentKey is not null && ReferenceListKeys.Contains(parentKey) &&
                    TryStableId(child, out var stableId)) {
                    array[i] = ResolveIndex(stableId, indices);
                }
                else {
                    DenormalizeReferences(child, indices, parentKey);
                }
            }
        }
    }

    private static JsonObject NodeValue(JsonObject node)
    {
        var value = node.CloneObject();
        value.Remove("GUID");
        return value;
    }

    private static JsonObject CommandValue(NormalizedCommand command)
    {
        var value = command.Command.CloneObject();
        value.Remove("GUID");
        value["Left Node Index"] = command.Left;
        value["Right Node Index"] = command.Right is null ? -1 : command.Right;
        return value;
    }

    private static NormalizedCommand CloneCommand(NormalizedCommand command) =>
        new(command.Command.CloneObject(), command.Left, command.Right);

    private static void MergeSection(string name, JsonNode vanilla, JsonNode input, Action<JsonNode?> assign,
        Dictionary<string, JsonNode?> changed, AsbMergeReport report)
    {
        if (JsonNode.DeepEquals(vanilla, input)) {
            return;
        }

        if (changed.TryGetValue(name, out var previous) && !JsonNode.DeepEquals(previous, input)) {
            report.SectionConflicts.Add(name);
        }

        changed[name] = input.DeepClone();
        assign(input);
    }

    private static void MergeBytesSection(string name, byte[]? vanilla, byte[]? input, Action<byte[]?> assign,
        Dictionary<string, JsonNode?> changed, AsbMergeReport report)
    {
        if (AsbJson.BytesEqual(vanilla, input)) {
            return;
        }

        var encoded = input is null ? null : JsonValue.Create(Convert.ToBase64String(input));
        if (changed.TryGetValue(name, out var previous) && !JsonNode.DeepEquals(previous, encoded)) {
            report.SectionConflicts.Add(name);
        }

        changed[name] = encoded;
        assign(input);
    }

    private static Dictionary<string, uint> BuildFinalIndices(uint vanillaCount, List<string> appendedOrder)
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var index = 0u; index < vanillaCount; index++) {
            result[VanillaId(index)] = index;
        }

        var next = vanillaCount;
        foreach (var stableId in appendedOrder) {
            if (result.TryAdd(stableId, next)) {
                next++;
            }
        }

        return result;
    }

    private static SortedDictionary<uint, JsonObject> FinalNodes(uint vanillaCount, List<string> appendedOrder,
        Dictionary<string, JsonObject> nodes, IReadOnlyDictionary<string, uint> indices)
    {
        var result = new SortedDictionary<uint, JsonObject>();
        for (var index = 0u; index < vanillaCount; index++) {
            var stableId = VanillaId(index);
            if (!nodes.TryGetValue(stableId, out var node)) {
                throw new InvalidDataException($"Merged ASB is missing vanilla node {index}.");
            }

            result[index] = FinalNode(node, indices);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stableId in appendedOrder) {
            if (!seen.Add(stableId)) continue;
            if (!indices.TryGetValue(stableId, out var index) || !nodes.TryGetValue(stableId, out var node)) {
                throw new InvalidDataException("Appended ASB node was not retained.");
            }

            result[index] = FinalNode(node, indices);
        }

        return result;
    }

    private static JsonObject FinalNode(JsonObject source, IReadOnlyDictionary<string, uint> indices)
    {
        var node = source.CloneObject();
        if (node["Body"] is JsonNode body) {
            DenormalizeReferences(body, indices, null);
        }

        return node;
    }

    private static JsonArray FinalCommands(List<string> order, Dictionary<string, NormalizedCommand> commands,
        IReadOnlyDictionary<string, uint> indices)
    {
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in order) {
            if (!seen.Add(name) || !commands.TryGetValue(name, out var normalized)) continue;
            var command = normalized.Command.CloneObject();
            command["Left Node Index"] = checked((ushort)ResolveIndex(normalized.Left, indices));
            command["Right Node Index"] = normalized.Right is null
                ? -1
                : checked((int)ResolveIndex(normalized.Right, indices));
            result.Add(command);
        }

        return result;
    }

    private static bool EventEquals(BaevEvent left, BaevEvent right) =>
        left.IsHoldEvent == right.IsHoldEvent && left.EventId == right.EventId &&
        left.Triggers.SequenceEqual(right.Triggers, BaevTriggerComparer.Instance) &&
        left.Holds.SequenceEqual(right.Holds, BaevHoldComparer.Instance);

    private static BaevEvent CloneEvent(BaevEvent value) => value with {
        Triggers = value.Triggers.Select(x => x with { Parameters = [.. x.Parameters] }).ToList(),
        Holds = value.Holds.Select(x => x with { Parameters = [.. x.Parameters] }).ToList()
    };

    private sealed class BaevTriggerComparer : IEqualityComparer<BaevTrigger>
    {
        public static readonly BaevTriggerComparer Instance = new();
        public bool Equals(BaevTrigger? x, BaevTrigger? y) =>
            x is not null && y is not null && x.StartFrame.Equals(y.StartFrame) &&
            x.Parameters.SequenceEqual(y.Parameters);
        public int GetHashCode(BaevTrigger obj) => HashCode.Combine(obj.StartFrame, obj.Parameters.Count);
    }

    private sealed class BaevHoldComparer : IEqualityComparer<BaevHold>
    {
        public static readonly BaevHoldComparer Instance = new();
        public bool Equals(BaevHold? x, BaevHold? y) =>
            x is not null && y is not null && x.StartFrame.Equals(y.StartFrame) &&
            x.EndFrame.Equals(y.EndFrame) && x.Parameters.SequenceEqual(y.Parameters);
        public int GetHashCode(BaevHold obj) => HashCode.Combine(obj.StartFrame, obj.EndFrame, obj.Parameters.Count);
    }

    private static string StableId(uint index, int? inputIndex, uint vanillaCount) =>
        index < vanillaCount ? VanillaId(index) : $"M{inputIndex ?? int.MaxValue}:{index}";

    private static string VanillaId(uint index) => $"V:{index}";

    private static uint ResolveIndex(string stableId, IReadOnlyDictionary<string, uint> indices) =>
        indices.TryGetValue(stableId, out var value)
            ? value
            : throw new InvalidDataException($"Unknown ASB node reference {stableId}.");

    private static bool TryStableId(JsonNode value, out string stableId)
    {
        if (value is JsonValue json && json.TryGetValue<string>(out var text) &&
            (text.StartsWith("V:", StringComparison.Ordinal) || text.StartsWith("M", StringComparison.Ordinal))) {
            stableId = text;
            return true;
        }

        stableId = "";
        return false;
    }

    private static bool TryUInt32(JsonNode value, out uint result)
    {
        try {
            result = AsbJson.UInt32(value);
            return true;
        }
        catch (InvalidDataException) {
            result = 0;
            return false;
        }
    }

    private static void SortDistinct(List<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        values.Clear();
        values.AddRange(distinct);
    }
}
