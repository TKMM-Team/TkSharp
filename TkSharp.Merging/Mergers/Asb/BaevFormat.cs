using System.Globalization;

namespace TkSharp.Merging.Mergers.Asb;

internal abstract record BaevParameter
{
    internal sealed record Integer(uint Value) : BaevParameter;
    internal sealed record Float(float Value) : BaevParameter;
    internal sealed record Vec3(float X, float Y, float Z) : BaevParameter;
    internal sealed record String(string Value) : BaevParameter;
}

internal sealed record BaevTrigger(float StartFrame, List<BaevParameter> Parameters);
internal sealed record BaevHold(float StartFrame, float EndFrame, List<BaevParameter> Parameters);
internal sealed record BaevEvent(List<BaevTrigger> Triggers, List<BaevHold> Holds, bool IsHoldEvent, uint EventId);
internal sealed record BaevNode(string Hash, uint Unknown, SortedDictionary<string, BaevEvent> Events);

internal sealed class BaevDocument
{
    public SortedDictionary<string, List<BaevNode>> Groups { get; } = new(StringComparer.Ordinal);

    public BaevDocument DeepClone()
    {
        var clone = new BaevDocument();
        foreach (var (hash, nodes) in Groups) {
            clone.Groups[hash] = nodes.Select(CloneNode).ToList();
        }

        return clone;
    }

    internal static BaevNode CloneNode(BaevNode node)
    {
        var events = new SortedDictionary<string, BaevEvent>(StringComparer.Ordinal);
        foreach (var (name, value) in node.Events) {
            events[name] = value with {
                Triggers = value.Triggers.Select(x => x with { Parameters = [.. x.Parameters] }).ToList(),
                Holds = value.Holds.Select(x => x with { Parameters = [.. x.Parameters] }).ToList()
            };
        }

        return node with { Events = events };
    }
}

internal static class BaevCodec
{
    private const int FileHeaderSize = 0xa8;
    private const int SectionHeaderSize = 0x28;
    private const int ContainerSize = 0x38;
    private const uint EventInfoSize = 0x18;
    private const uint EventHeaderSize = 0x30;
    private const uint EventEntrySize = 0x18;
    private const string ResourceName =
        "Nintendo.AnimationEvent.ResourceConverter.Resource.AnimationEventArchiveResData";

    private readonly record struct ArrayInfo(ulong Offset, uint Count, uint ElementSize);
    private readonly record struct EventInfo(string Hash, List<uint> NodeIndices);

    public static BaevDocument Read(ReadOnlyMemory<byte> data)
    {
        var reader = new BinaryDataReader(data);
        if (reader.ReadFixedString(4) != "BFFH") {
            throw new InvalidDataException("Invalid BAEV magic.");
        }

        reader.ReadUInt32();
        var fileSize = reader.ReadUInt32();
        if (fileSize > data.Length) {
            throw new InvalidDataException("BAEV header file size exceeds input.");
        }

        reader.ReadUInt32();
        var sectionArray = ReadArray(reader);
        var fallbackContainerOffset = reader.ReadUInt64();
        reader.Skip(0x80);

        ulong containerOffset = fallbackContainerOffset;
        if (sectionArray.Count > 0) {
            reader.Seek(CheckedOffset(sectionArray.Offset, data.Length));
            if (reader.ReadFixedString(4) != "BFSI") {
                throw new InvalidDataException("Invalid BAEV section header.");
            }

            reader.Skip(12);
            containerOffset = reader.ReadUInt64();
        }

        reader.Seek(CheckedOffset(containerOffset, data.Length));
        reader.Skip(24);
        var eventInfoArray = ReadArray(reader);
        var nodeArray = ReadArray(reader);

        var eventInfos = new List<EventInfo>(checked((int)eventInfoArray.Count));
        reader.Seek(CheckedOffset(eventInfoArray.Offset, data.Length));
        for (var i = 0u; i < eventInfoArray.Count; i++) {
            var hash = Hash(reader.ReadUInt32());
            reader.ReadUInt32();
            var indices = ReadArray(reader);
            var returnPosition = reader.Position;
            reader.Seek(CheckedOffset(indices.Offset, data.Length));
            var values = new List<uint>(checked((int)indices.Count));
            for (var j = 0u; j < indices.Count; j++) {
                values.Add(reader.ReadUInt32());
            }

            reader.Position = returnPosition;
            eventInfos.Add(new EventInfo(hash, values));
        }

        var nodes = new List<BaevNode>(checked((int)nodeArray.Count));
        reader.Seek(CheckedOffset(nodeArray.Offset, data.Length));
        for (var i = 0u; i < nodeArray.Count; i++) {
            nodes.Add(ReadNode(reader, data.Length));
        }

        var document = new BaevDocument();
        foreach (var info in eventInfos) {
            var group = new List<BaevNode>(info.NodeIndices.Count);
            foreach (var index in info.NodeIndices) {
                if (index >= nodes.Count) {
                    throw new InvalidDataException($"BAEV node index {index} exceeds table.");
                }

                group.Add(BaevDocument.CloneNode(nodes[(int)index]));
            }

            document.Groups[info.Hash] = group;
        }

        return document;
    }

    public static byte[] Write(BaevDocument document)
    {
        var strings = new StringPool();
        strings.Add("");
        foreach (var nodes in document.Groups.Values) {
            foreach (var node in nodes) {
                foreach (var (name, baevEvent) in node.Events) {
                    strings.Add(name);
                    foreach (var parameter in baevEvent.Triggers.SelectMany(x => x.Parameters)
                                 .Concat(baevEvent.Holds.SelectMany(x => x.Parameters))) {
                        if (parameter is BaevParameter.String text) {
                            strings.Add(text.Value);
                        }
                    }
                }
            }
        }

        var nodesFlat = new List<BaevNode>();
        var groups = new List<(string Hash, uint Start, int Count)>();
        foreach (var (hash, nodes) in document.Groups) {
            groups.Add((hash, checked((uint)nodesFlat.Count), nodes.Count));
            nodesFlat.AddRange(nodes);
        }

        var writer = new BinaryDataWriter { Position = FileHeaderSize + SectionHeaderSize * 2 };
        var containerOffset = writer.Position;
        writer.Position += ContainerSize;

        var eventInfoOffset = writer.Position;
        var indexPatches = new List<int>();
        foreach (var group in groups) {
            writer.Write(ParseHash(group.Hash));
            writer.Write(0u);
            indexPatches.Add(writer.ReserveUInt64());
            writer.Write(checked((uint)group.Count));
            writer.Write(4u);
        }

        for (var i = 0; i < groups.Count; i++) {
            writer.PatchUInt64(indexPatches[i], checked((ulong)writer.Position));
            var group = groups[i];
            for (var index = group.Start; index < group.Start + group.Count; index++) {
                writer.Write(index);
            }
        }

        writer.Align(8);
        var nodeOffset = writer.Position;
        var nodeEventPatches = new List<int>();
        foreach (var node in nodesFlat) {
            nodeEventPatches.Add(writer.ReserveUInt64());
            writer.Write(checked((uint)node.Events.Count));
            writer.Write(EventHeaderSize);
            writer.Write(ParseHash(node.Hash));
            writer.Write(node.Unknown);
        }

        var stringPatches = new List<(int Position, string Value)>();
        for (var i = 0; i < nodesFlat.Count; i++) {
            if (nodesFlat[i].Events.Count == 0) {
                continue;
            }

            writer.PatchUInt64(nodeEventPatches[i], checked((ulong)writer.Position));
            WriteNodeEvents(writer, nodesFlat[i], stringPatches);
        }

        var stringOffset = writer.Position;
        foreach (var (position, value) in stringPatches) {
            writer.PatchUInt64(position, checked((ulong)(stringOffset + strings.Offset(value))));
        }

        writer.Write(strings.Bytes);
        var fileSize = writer.Position;
        WriteFileHeader(writer, fileSize, containerOffset, stringOffset, strings.Bytes.Length);
        WriteContainer(writer, containerOffset, stringOffset, eventInfoOffset, groups.Count,
            nodeOffset, nodesFlat.Count);
        return writer.ToArray();
    }

    private static BaevNode ReadNode(BinaryDataReader reader, int length)
    {
        var eventArray = ReadArray(reader);
        var hash = Hash(reader.ReadUInt32());
        var unknown = reader.ReadUInt32();
        var returnPosition = reader.Position;
        reader.Seek(CheckedOffset(eventArray.Offset, length));
        var events = new SortedDictionary<string, BaevEvent>(StringComparer.Ordinal);
        for (var i = 0u; i < eventArray.Count; i++) {
            var (name, value) = ReadEvent(reader, length);
            events[name] = value;
        }

        reader.Position = returnPosition;
        return new BaevNode(hash, unknown, events);
    }

    private static (string Name, BaevEvent Event) ReadEvent(BinaryDataReader reader, int length)
    {
        var nameOffset = reader.ReadUInt64();
        var triggerArray = ReadArray(reader);
        var holdArray = ReadArray(reader);
        var isHold = reader.ReadUInt32() != 0;
        var eventId = reader.ReadUInt32();
        var name = reader.ReadCStringAt(CheckedOffset(nameOffset, length));
        return (name, new BaevEvent(
            ReadTriggers(reader, triggerArray, length),
            ReadHolds(reader, holdArray, length),
            isHold,
            eventId));
    }

    private static List<BaevTrigger> ReadTriggers(BinaryDataReader reader, ArrayInfo array, int length)
    {
        if (array.Count == 0) {
            return [];
        }

        var returnPosition = reader.Position;
        reader.Seek(CheckedOffset(array.Offset, length));
        var result = new List<BaevTrigger>(checked((int)array.Count));
        for (var i = 0u; i < array.Count; i++) {
            var parameters = ReadArray(reader);
            var start = reader.ReadSingle();
            reader.ReadSingle();
            result.Add(new BaevTrigger(start, ReadParameters(reader, parameters, length)));
        }

        reader.Position = returnPosition;
        return result;
    }

    private static List<BaevHold> ReadHolds(BinaryDataReader reader, ArrayInfo array, int length)
    {
        if (array.Count == 0) {
            return [];
        }

        var returnPosition = reader.Position;
        reader.Seek(CheckedOffset(array.Offset, length));
        var result = new List<BaevHold>(checked((int)array.Count));
        for (var i = 0u; i < array.Count; i++) {
            var parameters = ReadArray(reader);
            var start = reader.ReadSingle();
            var end = reader.ReadSingle();
            result.Add(new BaevHold(start, end, ReadParameters(reader, parameters, length)));
        }

        reader.Position = returnPosition;
        return result;
    }

    private static List<BaevParameter> ReadParameters(BinaryDataReader reader, ArrayInfo array, int length)
    {
        if (array.Count == 0) {
            return [];
        }

        var returnPosition = reader.Position;
        reader.Seek(CheckedOffset(array.Offset, length));
        var offsets = new List<ulong>(checked((int)array.Count));
        for (var i = 0u; i < array.Count; i++) {
            offsets.Add(reader.ReadUInt64());
        }

        var result = new List<BaevParameter>(offsets.Count);
        foreach (var offset in offsets) {
            reader.Seek(CheckedOffset(offset, length));
            var type = reader.ReadUInt32();
            reader.ReadUInt32();
            result.Add(type switch {
                0 => new BaevParameter.Integer(reader.ReadUInt32()),
                1 => new BaevParameter.Float(reader.ReadSingle()),
                3 => new BaevParameter.Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                5 => new BaevParameter.String(reader.ReadCStringAt(CheckedOffset(reader.ReadUInt64(), length))),
                _ => throw new InvalidDataException($"Unsupported BAEV parameter type {type}.")
            });
        }

        reader.Position = returnPosition;
        return result;
    }

    private static void WriteNodeEvents(BinaryDataWriter writer, BaevNode node,
        List<(int Position, string Value)> stringPatches)
    {
        var headers = new List<(BaevEvent Event, int TriggerPatch, int HoldPatch)>();
        foreach (var (name, value) in node.Events) {
            var namePatch = writer.ReserveUInt64();
            stringPatches.Add((namePatch, name));
            var triggerPatch = WriteEmptyArray(writer);
            var holdPatch = WriteEmptyArray(writer);
            writer.Write(value.IsHoldEvent || value.Holds.Count > 0 ? 1u : 0u);
            writer.Write(value.EventId);
            headers.Add((value, triggerPatch, holdPatch));
        }

        foreach (var header in headers) {
            WriteTriggers(writer, header.TriggerPatch, header.Event.Triggers, stringPatches);
            WriteHolds(writer, header.HoldPatch, header.Event.Holds, stringPatches);
        }
    }

    private static void WriteTriggers(BinaryDataWriter writer, int patch, List<BaevTrigger> entries,
        List<(int Position, string Value)> stringPatches)
    {
        if (entries.Count == 0) {
            return;
        }

        PatchArray(writer, patch, writer.Position, entries.Count, EventEntrySize);
        var arrays = new List<(int Patch, List<BaevParameter> Parameters)>();
        foreach (var entry in entries) {
            arrays.Add((WriteEmptyArray(writer), entry.Parameters));
            writer.Write(entry.StartFrame);
            writer.Write(0f);
        }

        WriteParameterArrays(writer, arrays, stringPatches);
    }

    private static void WriteHolds(BinaryDataWriter writer, int patch, List<BaevHold> entries,
        List<(int Position, string Value)> stringPatches)
    {
        if (entries.Count == 0) {
            return;
        }

        PatchArray(writer, patch, writer.Position, entries.Count, EventEntrySize);
        var arrays = new List<(int Patch, List<BaevParameter> Parameters)>();
        foreach (var entry in entries) {
            arrays.Add((WriteEmptyArray(writer), entry.Parameters));
            writer.Write(entry.StartFrame);
            writer.Write(entry.EndFrame);
        }

        WriteParameterArrays(writer, arrays, stringPatches);
    }

    private static void WriteParameterArrays(BinaryDataWriter writer,
        List<(int Patch, List<BaevParameter> Parameters)> arrays,
        List<(int Position, string Value)> stringPatches)
    {
        foreach (var (patch, parameters) in arrays) {
            if (parameters.Count == 0) {
                continue;
            }

            PatchArray(writer, patch, writer.Position, parameters.Count, 8);
            var pointers = parameters.Select(_ => writer.ReserveUInt64()).ToArray();
            for (var i = 0; i < parameters.Count; i++) {
                writer.PatchUInt64(pointers[i], checked((ulong)writer.Position));
                WriteParameter(writer, parameters[i], stringPatches);
            }
        }
    }

    private static void WriteParameter(BinaryDataWriter writer, BaevParameter parameter,
        List<(int Position, string Value)> stringPatches)
    {
        switch (parameter) {
            case BaevParameter.Integer integer:
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(integer.Value);
                writer.Write(0u);
                break;
            case BaevParameter.Float number:
                writer.Write(1u);
                writer.Write(0u);
                writer.Write(number.Value);
                writer.Write(0u);
                break;
            case BaevParameter.Vec3 vector:
                writer.Write(3u);
                writer.Write(0u);
                writer.Write(vector.X);
                writer.Write(vector.Y);
                writer.Write(vector.Z);
                writer.Write(0u);
                break;
            case BaevParameter.String text:
                writer.Write(5u);
                writer.Write(0u);
                stringPatches.Add((writer.ReserveUInt64(), text.Value));
                break;
        }
    }

    private static void WriteFileHeader(BinaryDataWriter writer, int fileSize, int containerOffset,
        int stringOffset, int stringSize)
    {
        writer.Position = 0;
        writer.Write("BFFH"u8);
        writer.Write(0u);
        writer.Write(checked((uint)fileSize));
        writer.Write(8u);
        WriteArray(writer, FileHeaderSize, 2, SectionHeaderSize);
        writer.Write(checked((ulong)containerOffset));
        writer.WriteFixedString(ResourceName, 0x80);
        WriteSectionHeader(writer, containerOffset, stringOffset - containerOffset, 8, containerOffset, "Default");
        WriteSectionHeader(writer, stringOffset, stringSize, 1, stringOffset, "StringPool");
    }

    private static void WriteSectionHeader(BinaryDataWriter writer, int offset, int size, uint alignment,
        int baseOffset, string name)
    {
        writer.Write("BFSI"u8);
        writer.Write(checked((uint)offset));
        writer.Write(checked((uint)size));
        writer.Write(alignment);
        writer.Write(checked((ulong)baseOffset));
        writer.WriteFixedString(name, 0x10);
    }

    private static void WriteContainer(BinaryDataWriter writer, int containerOffset, int stringOffset,
        int eventInfoOffset, int eventInfoCount, int nodeOffset, int nodeCount)
    {
        writer.Position = containerOffset;
        writer.Write(0ul);
        writer.Write(new byte[] { 0, 0, 1, 0 });
        writer.Write(0u);
        writer.Write(checked((ulong)stringOffset));
        WriteArray(writer, eventInfoCount == 0 ? 0 : eventInfoOffset, eventInfoCount,
            eventInfoCount == 0 ? 0 : EventInfoSize);
        WriteArray(writer, nodeCount == 0 ? 0 : nodeOffset, nodeCount,
            nodeCount == 0 ? 0 : EventInfoSize);
    }

    private static ArrayInfo ReadArray(BinaryDataReader reader) =>
        new(reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32());

    private static void WriteArray(BinaryDataWriter writer, int offset, int count, uint elementSize)
    {
        writer.Write(checked((ulong)offset));
        writer.Write(checked((uint)count));
        writer.Write(elementSize);
    }

    private static int WriteEmptyArray(BinaryDataWriter writer)
    {
        var position = writer.Position;
        WriteArray(writer, 0, 0, 0);
        return position;
    }

    private static void PatchArray(BinaryDataWriter writer, int patch, int offset, int count, uint elementSize)
    {
        writer.PatchUInt64(patch, checked((ulong)offset));
        writer.PatchUInt32(patch + 8, checked((uint)count));
        writer.PatchUInt32(patch + 12, elementSize);
    }

    private static int CheckedOffset(ulong value, int length)
    {
        if (value > (ulong)length) {
            throw new InvalidDataException($"BAEV offset 0x{value:x} exceeds file size 0x{length:x}.");
        }

        return checked((int)value);
    }

    private static string Hash(uint value) => $"0x{value:x8}";

    private static uint ParseHash(string value)
    {
        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan();
        return uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private sealed class StringPool
    {
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
        private readonly List<byte> _bytes = [];

        public byte[] Bytes => [.. _bytes];

        public void Add(string value)
        {
            if (_offsets.ContainsKey(value)) {
                return;
            }

            _offsets[value] = _bytes.Count;
            _bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            _bytes.Add(0);
        }

        public int Offset(string value) => _offsets.TryGetValue(value, out var offset)
            ? offset
            : throw new InvalidDataException($"BAEV string was not retained: {value}");
    }
}
