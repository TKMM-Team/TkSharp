using AsbLibrary;
using System.Collections;
using System.Reflection;

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
    // Appended node indices are local to each input; vanilla indices are shared.
    private readonly record struct NodeKey(int? InputIndex, uint SourceIndex)
    {
        public static NodeKey Vanilla(uint index) => new(null, index);

        public static NodeKey From(uint index, int? inputIndex, uint vanillaCount) =>
            index < vanillaCount
                ? Vanilla(index)
                : new(inputIndex
                      ?? throw new InvalidDataException(
                          $"Vanilla ASB references appended node {index}."),
                    index);

        public override string ToString() =>
            InputIndex is null ? $"V:{SourceIndex}" : $"M{InputIndex}:{SourceIndex}";
    }

    private sealed record SourceNode(AsbNode Value, int? InputIndex);
    private sealed record SourceCommand(AsbCommand Value, int? InputIndex);

    public static (AsbDocument Document, AsbMergeReport Report) Merge(
        AsbDocument vanilla,
        IReadOnlyList<AsbDocument> inputs)
    {
        var vanillaCount = checked((uint)vanilla.Nodes.Count);
        var baseNodes = IndexNodes(vanilla, null, vanillaCount);
        var baseCommands = IndexCommands(vanilla, null);
        var merged = vanilla.DeepClone();
        var mergedNodes = baseNodes.ToDictionary();
        var mergedCommands = baseCommands.ToDictionary();
        var commandOrder = vanilla.Commands.Select(command => command.Name).ToList();

        var changedNodes = new Dictionary<NodeKey, SourceNode>();
        var changedCommands = new Dictionary<string, SourceCommand>(StringComparer.Ordinal);
        var changedSections = new Dictionary<string, object?>(StringComparer.Ordinal);
        var appendedOrder = new List<NodeKey>();
        var report = new AsbMergeReport();

        for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            var input = inputs[inputIndex];
            if (input.Version != vanilla.Version)
            {
                throw new InvalidDataException(
                    $"ASB input {inputIndex} has version 0x{input.Version:x}, " +
                    $"expected 0x{vanilla.Version:x}.");
            }

            var inputNodes = IndexNodes(input, inputIndex, vanillaCount);
            var inputCommands = IndexCommands(input, inputIndex);

            for (var index = 0u; index < vanillaCount; index++)
            {
                var key = NodeKey.Vanilla(index);
                if (!inputNodes.TryGetValue(key, out var inputNode))
                {
                    continue;
                }

                var baseNode = baseNodes[key];
                if (AsbModelGraph.NodesEqual(
                        inputNode.Value,
                        inputNode.InputIndex,
                        baseNode.Value,
                        baseNode.InputIndex,
                        vanillaCount))
                {
                    continue;
                }

                if (changedNodes.TryGetValue(key, out var previous) &&
                    !AsbModelGraph.NodesEqual(
                        previous.Value,
                        previous.InputIndex,
                        inputNode.Value,
                        inputNode.InputIndex,
                        vanillaCount))
                {
                    report.NodeConflicts.Add(key.ToString());
                }

                changedNodes[key] = inputNode;
                mergedNodes[key] = inputNode;
            }

            foreach (var (key, node) in inputNodes)
            {
                if (key.InputIndex is null)
                {
                    continue;
                }

                appendedOrder.Add(key);
                mergedNodes[key] = node;
            }

            foreach (var (name, command) in inputCommands)
            {
                var changed = !baseCommands.TryGetValue(name, out var baseCommand) ||
                              !AsbModelGraph.CommandsEqual(
                                  command.Value,
                                  command.InputIndex,
                                  baseCommand.Value,
                                  baseCommand.InputIndex,
                                  vanillaCount);
                if (!changed)
                {
                    continue;
                }

                if (changedCommands.TryGetValue(name, out var previous) &&
                    !AsbModelGraph.CommandsEqual(
                        previous.Value,
                        previous.InputIndex,
                        command.Value,
                        command.InputIndex,
                        vanillaCount))
                {
                    report.CommandConflicts.Add(name);
                }

                if (!mergedCommands.ContainsKey(name))
                {
                    commandOrder.Add(name);
                    report.AddedCommands++;
                }

                changedCommands[name] = command;
                mergedCommands[name] = command;
            }

            MergeSection(
                "Local Blackboard Parameters",
                vanilla.Blackboard,
                input.Blackboard,
                value => merged.Blackboard = value,
                changedSections,
                report);
            MergeSection(
                "Transitions",
                vanilla.Transitions,
                input.Transitions,
                value => merged.Transitions = value,
                changedSections,
                report);
            MergeSection(
                "Animation Slots",
                vanilla.AnimationSlots,
                input.AnimationSlots,
                value => merged.AnimationSlots = value,
                changedSections,
                report);
            MergeSection(
                "Valid Tag List",
                vanilla.ValidTags,
                input.ValidTags,
                value => merged.ValidTags = value,
                changedSections,
                report);
            MergeSection(
                "0x68 Section",
                vanilla.X68,
                input.X68,
                value => merged.X68 = value,
                changedSections,
                report);
            MergeBytesSection(
                "EXB Section",
                vanilla.Exb,
                input.Exb,
                value => merged.Exb = value,
                changedSections,
                report);
        }

        var appendedKeys = appendedOrder.Distinct().ToList();
        report.AppendedNodes = appendedKeys.Count;
        var finalIndices = BuildFinalIndices(vanillaCount, appendedKeys);
        merged.Nodes = BuildFinalNodes(
            vanillaCount,
            appendedKeys,
            mergedNodes,
            finalIndices);
        merged.Commands = BuildFinalCommands(
            commandOrder,
            mergedCommands,
            vanillaCount,
            finalIndices);

        var guidOwners = new Dictionary<Guid, uint>();
        for (var index = 0; index < merged.Nodes.Count; index++)
        {
            var guid = merged.Nodes[index].Guid;
            if (guidOwners.TryGetValue(guid, out var previous))
            {
                report.GuidConflicts.Add($"{guid} (nodes {previous} and {index})");
            }
            else
            {
                guidOwners[guid] = checked((uint)index);
            }
        }

        SortDistinct(report.NodeConflicts);
        SortDistinct(report.CommandConflicts);
        SortDistinct(report.SectionConflicts);
        SortDistinct(report.GuidConflicts);
        return (merged, report);
    }

    public static (BaevDocument Document, BaevMergeReport Report) Merge(
        BaevDocument vanilla,
        IReadOnlyList<BaevDocument> inputs)
    {
        var merged = vanilla.DeepClone();
        var changedUnknowns = new Dictionary<string, uint>(StringComparer.Ordinal);
        var changedEvents = new Dictionary<string, BaevEvent>(StringComparer.Ordinal);
        var report = new BaevMergeReport();

        foreach (var input in inputs)
        {
            foreach (var (groupHash, inputNodes) in input.Groups)
            {
                foreach (var inputNode in inputNodes)
                {
                    var baseNode = vanilla.Groups.TryGetValue(groupHash, out var vanillaNodes)
                        ? vanillaNodes.FirstOrDefault(node => node.Hash == inputNode.Hash)
                        : null;
                    if (!merged.Groups.TryGetValue(groupHash, out var mergedGroup))
                    {
                        mergedGroup = [];
                        merged.Groups[groupHash] = mergedGroup;
                    }

                    var nodeIndex = mergedGroup.FindIndex(node => node.Hash == inputNode.Hash);
                    if (nodeIndex < 0)
                    {
                        mergedGroup.Add(BaevDocument.CloneNode(inputNode));
                        report.AddedNodes++;
                        report.AddedEvents += inputNode.Events.Count;
                        continue;
                    }

                    var nodeKey = $"{groupHash}/{inputNode.Hash}";
                    if (baseNode is null || baseNode.Unknown != inputNode.Unknown)
                    {
                        if (changedUnknowns.TryGetValue(nodeKey, out var previous) &&
                            previous != inputNode.Unknown)
                        {
                            report.NodeConflicts.Add(nodeKey);
                        }

                        changedUnknowns[nodeKey] = inputNode.Unknown;
                        mergedGroup[nodeIndex] =
                            mergedGroup[nodeIndex] with { Unknown = inputNode.Unknown };
                    }

                    foreach (var (eventName, inputEvent) in inputNode.Events)
                    {
                        var unchanged = baseNode is not null &&
                                        baseNode.Events.TryGetValue(eventName, out var baseEvent) &&
                                        EventEquals(baseEvent, inputEvent);
                        if (unchanged)
                        {
                            continue;
                        }

                        var eventKey = $"{nodeKey}/{eventName}";
                        if (changedEvents.TryGetValue(eventKey, out var previous) &&
                            !EventEquals(previous, inputEvent))
                        {
                            report.EventConflicts.Add(eventKey);
                        }

                        var currentEvents = mergedGroup[nodeIndex].Events;
                        if (!currentEvents.ContainsKey(eventName))
                        {
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

    private static Dictionary<NodeKey, SourceNode> IndexNodes(
        AsbDocument document,
        int? inputIndex,
        uint vanillaCount)
    {
        var result = new Dictionary<NodeKey, SourceNode>();
        for (var index = 0; index < document.Nodes.Count; index++)
        {
            var key = NodeKey.From(checked((uint)index), inputIndex, vanillaCount);
            result[key] = new SourceNode(document.Nodes[index], inputIndex);
        }

        return result;
    }

    private static Dictionary<string, SourceCommand> IndexCommands(
        AsbDocument document,
        int? inputIndex)
    {
        var result = new Dictionary<string, SourceCommand>(StringComparer.Ordinal);
        foreach (var command in document.Commands)
        {
            result[command.Name] = new SourceCommand(command, inputIndex);
        }

        return result;
    }

    private static void MergeSection<T>(
        string name,
        T vanilla,
        T input,
        Action<T> assign,
        IDictionary<string, object?> changed,
        AsbMergeReport report)
        where T : class
    {
        if (AsbModelGraph.ValuesEqual(vanilla, input))
        {
            return;
        }

        if (changed.TryGetValue(name, out var previous) &&
            !AsbModelGraph.ValuesEqual(previous, input))
        {
            report.SectionConflicts.Add(name);
        }

        var copy = AsbModelGraph.Clone(input);
        changed[name] = copy;
        assign(copy);
    }

    private static void MergeBytesSection(
        string name,
        byte[]? vanilla,
        byte[]? input,
        Action<byte[]?> assign,
        IDictionary<string, object?> changed,
        AsbMergeReport report)
    {
        if (BytesEqual(vanilla, input))
        {
            return;
        }

        if (changed.TryGetValue(name, out var previous) &&
            !BytesEqual(previous as byte[], input))
        {
            report.SectionConflicts.Add(name);
        }

        var copy = input?.ToArray();
        changed[name] = copy;
        assign(copy);
    }

    private static Dictionary<NodeKey, uint> BuildFinalIndices(
        uint vanillaCount,
        IReadOnlyList<NodeKey> appendedOrder)
    {
        var result = new Dictionary<NodeKey, uint>();
        for (var index = 0u; index < vanillaCount; index++)
        {
            result[NodeKey.Vanilla(index)] = index;
        }

        var next = vanillaCount;
        foreach (var key in appendedOrder)
        {
            result[key] = next++;
        }

        return result;
    }

    private static List<AsbNode> BuildFinalNodes(
        uint vanillaCount,
        IReadOnlyList<NodeKey> appendedOrder,
        IReadOnlyDictionary<NodeKey, SourceNode> nodes,
        IReadOnlyDictionary<NodeKey, uint> finalIndices)
    {
        var result = new List<AsbNode>(
            checked((int)vanillaCount + appendedOrder.Count));

        for (var index = 0u; index < vanillaCount; index++)
        {
            var key = NodeKey.Vanilla(index);
            if (!nodes.TryGetValue(key, out var node))
            {
                throw new InvalidDataException($"Merged ASB is missing vanilla node {index}.");
            }

            result.Add(FinalizeNode(node, vanillaCount, finalIndices));
        }

        foreach (var key in appendedOrder)
        {
            if (!nodes.TryGetValue(key, out var node))
            {
                throw new InvalidDataException("Appended ASB node was not retained.");
            }

            result.Add(FinalizeNode(node, vanillaCount, finalIndices));
        }

        return result;
    }

    private static AsbNode FinalizeNode(
        SourceNode source,
        uint vanillaCount,
        IReadOnlyDictionary<NodeKey, uint> finalIndices)
    {
        var node = source.Value.DeepClone();
        if (node.Body is not null)
        {
            AsbModelGraph.RemapNodeReferences(
                node.Body,
                index => ResolveIndex(
                    NodeKey.From(index, source.InputIndex, vanillaCount),
                    finalIndices));
        }

        return node;
    }

    private static List<AsbCommand> BuildFinalCommands(
        IEnumerable<string> order,
        IReadOnlyDictionary<string, SourceCommand> commands,
        uint vanillaCount,
        IReadOnlyDictionary<NodeKey, uint> finalIndices)
    {
        var result = new List<AsbCommand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in order)
        {
            if (!seen.Add(name) || !commands.TryGetValue(name, out var source))
            {
                continue;
            }

            var command = AsbModelGraph.Clone(source.Value);
            command.LeftNodeIndex = ResolveIndex(
                NodeKey.From(command.LeftNodeIndex, source.InputIndex, vanillaCount),
                finalIndices);
            if (command.RightNodeIndex is { } right)
            {
                command.RightNodeIndex = ResolveIndex(
                    NodeKey.From(right, source.InputIndex, vanillaCount),
                    finalIndices);
            }

            result.Add(command);
        }

        return result;
    }

    private static bool EventEquals(BaevEvent left, BaevEvent right) =>
        left.IsHoldEvent == right.IsHoldEvent &&
        left.EventId == right.EventId &&
        left.Triggers.SequenceEqual(right.Triggers, BaevTriggerComparer.Instance) &&
        left.Holds.SequenceEqual(right.Holds, BaevHoldComparer.Instance);

    private static BaevEvent CloneEvent(BaevEvent value) => value with
    {
        Triggers = value.Triggers
            .Select(trigger => trigger with { Parameters = [.. trigger.Parameters] })
            .ToList(),
        Holds = value.Holds
            .Select(hold => hold with { Parameters = [.. hold.Parameters] })
            .ToList()
    };

    private sealed class BaevTriggerComparer : IEqualityComparer<BaevTrigger>
    {
        public static readonly BaevTriggerComparer Instance = new();

        public bool Equals(BaevTrigger? left, BaevTrigger? right) =>
            left is not null &&
            right is not null &&
            left.StartFrame.Equals(right.StartFrame) &&
            left.Parameters.SequenceEqual(right.Parameters);

        public int GetHashCode(BaevTrigger value) =>
            HashCode.Combine(value.StartFrame, value.Parameters.Count);
    }

    private sealed class BaevHoldComparer : IEqualityComparer<BaevHold>
    {
        public static readonly BaevHoldComparer Instance = new();

        public bool Equals(BaevHold? left, BaevHold? right) =>
            left is not null &&
            right is not null &&
            left.StartFrame.Equals(right.StartFrame) &&
            left.EndFrame.Equals(right.EndFrame) &&
            left.Parameters.SequenceEqual(right.Parameters);

        public int GetHashCode(BaevHold value) =>
            HashCode.Combine(value.StartFrame, value.EndFrame, value.Parameters.Count);
    }

    private static bool BytesEqual(byte[]? left, byte[]? right) =>
        left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);

    private static uint ResolveIndex(
        NodeKey key,
        IReadOnlyDictionary<NodeKey, uint> indices) =>
        indices.TryGetValue(key, out var value)
            ? value
            : throw new InvalidDataException($"Unknown ASB node reference {key}.");

    private static void SortDistinct(List<string> values)
    {
        var distinct = values
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        values.Clear();
        values.AddRange(distinct);
    }

    // Reference annotations let comparisons and rewrites follow the typed model without name-based rules.
    private static class AsbModelGraph
    {
        public static T Clone<T>(T value) where T : class =>
            (T)CloneObject(value)!;

        public static bool ValuesEqual(object? left, object? right) =>
            Compare(left, null, right, null, 0, false, false);

        public static bool NodesEqual(
            AsbNode left,
            int? leftInput,
            AsbNode right,
            int? rightInput,
            uint vanillaCount) =>
            Compare(left, leftInput, right, rightInput, vanillaCount, true, true);

        public static bool CommandsEqual(
            AsbCommand left,
            int? leftInput,
            AsbCommand right,
            int? rightInput,
            uint vanillaCount) =>
            Compare(left, leftInput, right, rightInput, vanillaCount, true, true);

        public static void RemapNodeReferences(
            object value,
            Func<uint, uint> resolve)
        {
            RemapObject(value, resolve);
        }

        private static bool Compare(
            object? left,
            int? leftInput,
            object? right,
            int? rightInput,
            uint vanillaCount,
            bool ignoreRootGuid,
            bool isRoot)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null || left.GetType() != right.GetType())
            {
                return false;
            }

            var type = left.GetType();
            if (type.IsValueType || left is string)
            {
                return left.Equals(right);
            }

            if (left is byte[] leftBytes && right is byte[] rightBytes)
            {
                return leftBytes.AsSpan().SequenceEqual(rightBytes);
            }

            if (left is IList leftList && right is IList rightList)
            {
                if (leftList.Count != rightList.Count)
                {
                    return false;
                }

                for (var i = 0; i < leftList.Count; i++)
                {
                    if (!Compare(
                            leftList[i],
                            leftInput,
                            rightList[i],
                            rightInput,
                            vanillaCount,
                            false,
                            false))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var property in ReadableProperties(type))
            {
                if (isRoot && ignoreRootGuid && property.Name == "Guid")
                {
                    continue;
                }

                var leftValue = property.GetValue(left);
                var rightValue = property.GetValue(right);
                if (property.IsDefined(typeof(AsbNodeReferenceAttribute)) &&
                    !ReferencesEqual(
                        leftValue,
                        leftInput,
                        rightValue,
                        rightInput,
                        vanillaCount))
                {
                    return false;
                }

                if (!property.IsDefined(typeof(AsbNodeReferenceAttribute)) &&
                    !Compare(
                        leftValue,
                        leftInput,
                        rightValue,
                        rightInput,
                        vanillaCount,
                        false,
                        false))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ReferencesEqual(
            object? left,
            int? leftInput,
            object? right,
            int? rightInput,
            uint vanillaCount)
        {
            if (TryNodeIndex(left, out var leftIndex) &&
                TryNodeIndex(right, out var rightIndex))
            {
                return NodeKey.From(leftIndex, leftInput, vanillaCount) ==
                       NodeKey.From(rightIndex, rightInput, vanillaCount);
            }

            if (left is IList leftList && right is IList rightList)
            {
                if (leftList.Count != rightList.Count)
                {
                    return false;
                }

                for (var i = 0; i < leftList.Count; i++)
                {
                    if (leftList[i] is not uint leftItem ||
                        rightList[i] is not uint rightItem ||
                        NodeKey.From(leftItem, leftInput, vanillaCount) !=
                        NodeKey.From(rightItem, rightInput, vanillaCount))
                    {
                        return false;
                    }
                }

                return true;
            }

            return left is null && right is null;
        }

        private static bool TryNodeIndex(object? value, out uint index)
        {
            switch (value)
            {
                case uint value32:
                    index = value32;
                    return true;
                case ushort value16:
                    index = value16;
                    return true;
                default:
                    index = 0;
                    return false;
            }
        }

        private static void RemapObject(
            object? value,
            Func<uint, uint> resolve)
        {
            if (value is null || value is string || value.GetType().IsValueType)
            {
                return;
            }

            if (value is IList list)
            {
                foreach (var item in list)
                {
                    RemapObject(item, resolve);
                }

                return;
            }

            foreach (var property in ReadableProperties(value.GetType()))
            {
                var child = property.GetValue(value);
                if (property.IsDefined(typeof(AsbNodeReferenceAttribute)))
                {
                    if (child is uint index && property.CanWrite)
                    {
                        property.SetValue(value, resolve(index));
                    }
                    else if (child is ushort shortIndex && property.CanWrite)
                    {
                        property.SetValue(value, checked((ushort)resolve(shortIndex)));
                    }
                    else if (child is IList references)
                    {
                        for (var i = 0; i < references.Count; i++)
                        {
                            if (references[i] is uint reference)
                            {
                                references[i] = resolve(reference);
                            }
                        }
                    }

                    continue;
                }

                RemapObject(child, resolve);
            }
        }

        private static object? CloneObject(object? value)
        {
            if (value is null || value is string || value.GetType().IsValueType)
            {
                return value;
            }

            if (value is byte[] bytes)
            {
                return bytes.ToArray();
            }

            if (value is IList list)
            {
                var copy = (IList)Activator.CreateInstance(value.GetType())!;
                foreach (var item in list)
                {
                    copy.Add(CloneObject(item));
                }

                return copy;
            }

            var type = value.GetType();
            var result = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException(
                    $"Cannot clone ASB model type {type.FullName}.");
            foreach (var property in type
                         .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(property => property.CanRead && property.CanWrite))
            {
                property.SetValue(result, CloneObject(property.GetValue(value)));
            }

            return result;
        }

        private static IEnumerable<PropertyInfo> ReadableProperties(Type type) =>
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead);
    }
}