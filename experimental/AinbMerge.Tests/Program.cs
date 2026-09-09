using System.Buffers.Binary;
using System.Text.Json;
using AinbModel.Contract;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers;
using TkSharp.Merging.Mergers.Ainb;

namespace AinbMerge.Tests;

internal static class Program
{
    private static int _passed, _failed;
    private static int Main(string[] args)
    {
        Run("independent properties; immutable inputs", () => {
            var b = Graph(); var low = Property(b, 0, 2); var high = Property(b, 1, 3);
            var merged = AinbGraphMerger.Merge(b, [low, high]);
            Equal(new AinbFloat(2), merged.Document.Nodes[1].Properties[0].Value);
            Equal(new AinbFloat(3), merged.Document.Nodes[1].Properties[1].Value);
            Equal(new AinbFloat(1), b.Nodes[1].Properties[0].Value);
            Equal(0, merged.Report.Conflicts.Count);
        });
        Run("property conflict both priorities", () => {
            var b = Graph(); var low = Property(b, 0, 2); var high = Property(b, 0, 3);
            foreach (var mods in new[] { new[] { low, high }, new[] { high, low } }) {
                var result = AinbGraphMerger.Merge(b, mods, ["low", "high"]);
                Equal(mods[1].Nodes[1].Properties[0].Value, result.Document.Nodes[1].Properties[0].Value);
                Equal("high", result.Report.Conflicts.Single().Source);
            }
        });
        Run("reindexed nodes and regenerated GUIDs", () => {
            var b = Graph(); var mod = Reorder(b, [2, 0, 1]);
            Equal(AinbGraphMerger.WithoutGuids(b), AinbGraphMerger.WithoutGuids(AinbGraphMerger.Merge(b, [mod]).Document));
        });
        Run("same GUID additions stay separate", () => {
            var b = Graph(); var result = AinbGraphMerger.Merge(b, [Add(b, "Low"), Add(b, "High")]);
            Equal(5, result.Document.Nodes.Count);
            Equal(5, result.Document.Nodes.Select(n => n.Id).Distinct().Count());
            True(result.Report.Additions.All(a => a.ReachableFromCommands == 1));
        });
        Run("identical mod idempotence", () => {
            var b = Graph(); var mod = Add(b, "Added");
            Equal(AinbGraphMerger.Merge(b, [mod]).Document, AinbGraphMerger.Merge(b, [mod, mod]).Document);
        });
        Run("unchanged high priority does not undo low", () => {
            var b = Graph(); Equal(4, AinbGraphMerger.Merge(b, [Add(b, "Added"), b]).Document.Nodes.Count);
        });
        Run("replacement plus append, both orders", () => {
            var b = Graph(); var armor = Replace(b, "Armor"); var shinobi = Add(b, "Shinobi");
            foreach (var mods in new[] { new[] { armor, shinobi }, new[] { shinobi, armor } }) {
                var result = AinbGraphMerger.Merge(b, mods);
                Equal("TaskA,Armor,Shinobi", ChildNames(result.Document));
                Equal(1, result.Document.Nodes.Count(n => n.Name == "TaskB"));
                True(result.Report.Additions.All(a => a.ReachableFromCommands == 1));
                Equal(0, result.Report.Conflicts.Count);
            }
        });
        Run("competing replacements retain independent appends", () => {
            var b = Graph(); var low = Add(Replace(b, "LowReplacement"), "LowTail"); var high = Add(Replace(b, "HighReplacement"), "HighTail");
            var result = AinbGraphMerger.Merge(b, [low, high]);
            Equal("TaskA,HighReplacement,LowTail,HighTail", ChildNames(result.Document));
            Equal(1, result.Report.Conflicts.Count);
        });
        Run("replacement append idempotence after composition", () => {
            var b = Graph(); var low = Replace(b, "Armor"); var high = Add(b, "Shinobi");
            Equal(AinbGraphMerger.Merge(b, [low, high]).Document, AinbGraphMerger.Merge(b, [low, high, low, high]).Document);
        });
        Run("sequential lists stay atomic", () => {
            var b = Update(Graph(), 0, n => n with { Type = AinbNodeType.Sequential });
            var result = AinbGraphMerger.Merge(b, [Add(b, "Low"), Add(b, "High")]);
            Equal("TaskA,TaskB,High", ChildNames(result.Document));
            Equal(0, result.Report.Additions[0].ReachableFromCommands);
        });
        Run("changed execution policies prevent composition", () => {
            var b = Graph(); var high = Update(Add(b, "High"), 0, n => n with {
                Properties = n.Properties.Select((p, i) => i == 0 ? p with { Value = new AinbInt(1) } : p).ToAinbList()
            });
            var result = AinbGraphMerger.Merge(b, [Add(b, "Low"), high]);
            Equal("TaskA,TaskB,High", ChildNames(result.Document));
        });
        Run("reordered child list stays atomic", () => {
            var b = Graph(); var high = Update(b, 0, n => n with { Plugs = n.Plugs.Reverse().ToAinbList() });
            Equal("TaskB,TaskA", ChildNames(AinbGraphMerger.Merge(b, [Add(b, "Low"), high]).Document));
        });
        Run("duplicate child targets stay atomic", () => {
            var b = Graph(); var high = Update(b, 0, n => n with { Plugs = n.Plugs.Append(n.Plugs[1]).ToAinbList() });
            Equal("TaskA,TaskB,TaskB", ChildNames(AinbGraphMerger.Merge(b, [Add(b, "Low"), high]).Document));
        });
        Run("deleted node and edge", () => {
            var b = Graph(); Equal(2, AinbGraphMerger.Merge(b, [DeleteB(b)]).Document.Nodes.Count);
        });
        Run("dangling dependency rejects composition", () => {
            var b = Graph(); var low = Update(Add(b, "Dependent"), 3, n => n with {
                Plugs = new(new[] { new AinbPlug(AinbPlugType.Generic, 2, "Input") })
            });
            Throws<AinbMergeNotSupportedException>(() => AinbGraphMerger.Merge(b, [low, DeleteB(b)]));
        });
        Run("ambiguous repeated nodes rejected", () => {
            var b = Update(Update(Graph(), 1, n => n with { Name = "Repeated", Properties = new() }), 2, n => n with { Name = "Repeated" });
            var mod = Update(b, 1, n => n with { Properties = new(new[] { new AinbProperty(AinbDataType.Int, "X", new AinbInt(1)) }) });
            Throws<AinbMergeNotSupportedException>(() => AinbGraphMerger.Merge(b, [mod]));
        });
        Run("unchanged repeated nodes skip matching", () => {
            var b = Update(Update(Graph(), 1, n => n with { Name = "Repeated", Properties = new() }), 2, n => n with { Name = "Repeated" });
            Equal(b, AinbGraphMerger.Merge(b, [b]).Document);
        });
        Run("output layout change rejected", () => {
            var b = Graph(); var mod = Update(b, 1, n => n with { Outputs = new(new[] { new AinbOutput(AinbDataType.Bool, "Value", true, null) }) });
            Throws<AinbMergeNotSupportedException>(() => AinbGraphMerger.Merge(b, [mod]));
        });
        Run("input sources reindex with their producers", () => {
            var b = Update(Graph(), 2, n => n with { Outputs = new(new[] { new AinbOutput(AinbDataType.Bool, "Value", true, null) }) });
            b = Update(b, 1, n => n with { Inputs = new(new[] { new AinbInput(AinbDataType.Bool, "Input", new AinbBool(false), 0, null, null, new(2, 0), new()) }) });
            Equal(AinbGraphMerger.WithoutGuids(b), AinbGraphMerger.WithoutGuids(AinbGraphMerger.Merge(b, [Reorder(b, [2, 0, 1])]).Document));
        });
        Run("invalid query target fails validation", () => {
            var b = Graph(); var bad = Update(b, 0, n => n with { Queries = new(new[] { 1 }) });
            Throws<InvalidDataException>(() => AinbGraphMerger.Merge(b, [bad]));
        });
        Run("blackboard IDs resolve as a pair", () => {
            var b = Graph(); var result = AinbGraphMerger.Merge(b, [b with { BlackboardId = 1 }, b with { ParentBlackboardId = 2 }]);
            Equal(0u, result.Document.BlackboardId); Equal(2u, result.Document.ParentBlackboardId);
        });
        Run("explicit unsupported feature cannot be silently lost", () => {
            var b = Graph(); Throws<AinbMergeNotSupportedException>(() => AinbGraphMerger.Merge(b, [b with { UnsupportedFeatures = AinbUnsupportedFeatures.Expressions }]));
        });
        Run("unrepresented flags rejected", () => {
            var b = Graph(); var mod = Update(b, 1, n => n with {
                Properties = n.Properties.Select(p => p with { Flags = (AinbParameterFlags)128 }).ToAinbList()
            });
            Throws<AinbMergeNotSupportedException>(() => AinbGraphMerger.Merge(b, [mod]));
        });
        Run("parameter type mismatch fails validation", () => {
            var b = Graph(); var mod = Update(b, 1, n => n with {
                Properties = n.Properties.Select(p => p with { Value = new AinbInt(1) }).ToAinbList()
            });
            Throws<InvalidDataException>(() => AinbGraphMerger.Merge(b, [mod]));
        });
        Run("non-finite parameter value fails validation", () => {
            var b = Graph(); Throws<InvalidDataException>(() => AinbGraphMerger.Merge(b, [Property(b, 0, float.NaN)]));
        });
        Run("binary boundary no vanilla uses exact highest input", () => {
            SnapshotCodec codec = new(); var b = Graph(); var low = codec.Register(Add(b, "Low")); var high = codec.Register(Add(b, "High"));
            var result = new AinbBinaryMerger(codec).Merge([], [low, high], ["low", "high"]);
            True(result.Bytes.SequenceEqual(high)); Equal(AinbMergeStatus.Fallback, result.Report.Status); Equal(0, codec.Writes);
            Throws<AinbMergeNotSupportedException>(() => new AinbBinaryMerger(codec).Merge([], [low, high], allowFallback: false));
        });
        Run("binary boundary roundtrip and input reuse", () => {
            SnapshotCodec codec = new(); var b = Graph(); var baseline = codec.Register(b); var low = codec.Register(Replace(b, "Armor")); var high = codec.Register(Add(b, "Shinobi"));
            var merger = new AinbBinaryMerger(codec);
            var result = merger.Merge(baseline, [low, high]);
            Equal("TaskA,Armor,Shinobi", ChildNames(codec.Read(result.Bytes))); Equal(1, codec.Writes);
            var one = merger.Merge(baseline, [high]); True(one.Bytes.SequenceEqual(high)); Equal(1, codec.Writes);
        });
        Run("binary boundary reports exact fallback", () => {
            SnapshotCodec codec = new(); var b = Graph(); var baseline = codec.Register(b); var bad = codec.Register(b with { UnsupportedFeatures = AinbUnsupportedFeatures.Blackboard });
            var result = new AinbBinaryMerger(codec).Merge(baseline, [bad]);
            True(result.Bytes.SequenceEqual(bad)); Equal(AinbMergeStatus.Fallback, result.Report.Status);
        });
        Run("malformed codec input is not copied", () => {
            SnapshotCodec codec = new(); Throws<InvalidDataException>(() => new AinbBinaryMerger(codec).Merge(codec.Register(Graph()), [new byte[] { 0 }]));
        });
        Run("writer mismatch fails instead of emitting output", () => {
            SnapshotCodec codec = new() { CorruptWrites = true }; var b = Graph();
            Throws<InvalidDataException>(() => new AinbBinaryMerger(codec).Merge(codec.Register(b), [codec.Register(Add(b, "Low")), codec.Register(Add(b, "High"))]));
        });
        Run("ITkMerger enumerable and rented-buffer paths agree", () => {
            SnapshotCodec codec = new(); var b = Graph(); var baseline = codec.Register(b); var low = codec.Register(Replace(b, "Armor")); var high = codec.Register(Add(b, "Shinobi"));
            AinbMerger merger = new(codec); TkChangelogEntry entry = new("AI/Experiment.module.ainb", ChangelogEntryType.Copy, 0, 0);
            using MemoryStream a = new(); using MemoryStream c = new();
            merger.Merge(entry, new ArraySegment<byte>[] { low, high }, baseline, a);
            using var buffers = RentedBuffers<byte>.Allocate(new Stream[] { new MemoryStream(low), new MemoryStream(high) }, disposeStreams: true);
            merger.Merge(entry, buffers, baseline, c);
            True(a.ToArray().SequenceEqual(c.ToArray()));
            using MemoryStream single = new(); merger.MergeSingle(entry, high, baseline, single); True(high.SequenceEqual(single.ToArray()));
        });

        if (args.Length == 2 && args[0] == "--fixtures") RunFixtures(args[1]);
        else if (args.Length != 0) { Console.Error.WriteLine("Usage: [--fixtures directory]"); return 2; }
        else Console.WriteLine("Private fixture parity was not requested.");
        Console.WriteLine($"{_passed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void RunFixtures(string root)
    {
        using var cases = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "cases.json")));
        foreach (var c in cases.RootElement.EnumerateArray()) Run(c.GetProperty("name").GetString()!, () => {
            string directory = Path.Combine(root, c.GetProperty("directory").GetString()!);
            var b = FixtureJson.Read(Path.Combine(directory, "base.json"));
            var low = FixtureJson.Read(Path.Combine(directory, "low.json"));
            var high = FixtureJson.Read(Path.Combine(directory, "high.json"));
            var expected = FixtureJson.Read(Path.Combine(directory, "expected.json"));
            SnapshotCodec codec = new();
            var result = new AinbBinaryMerger(codec).Merge(codec.Register(b), [codec.Register(low), codec.Register(high)]);
            var actual = codec.Read(result.Bytes);
            Equal(AinbGraphMerger.WithoutGuids(expected), AinbGraphMerger.WithoutGuids(actual));
            var status = c.GetProperty("status").GetString() switch {
                "merged" => AinbMergeStatus.Merged, "fallback" => AinbMergeStatus.Fallback,
                "input-reused" => AinbMergeStatus.InputReused, "unchanged" => AinbMergeStatus.Unchanged,
                _ => throw new InvalidDataException("Unexpected Python status")
            };
            Equal(status, result.Report.Status);
            Equal(c.GetProperty("conflicts").GetInt32(), result.Report.Conflicts.Count);
            var additions = c.GetProperty("additions").EnumerateArray().ToArray();
            Equal(additions.Length, result.Report.Additions.Count);
            for (int i = 0; i < additions.Length; i++) {
                Equal(additions[i].GetProperty("retained").GetInt32(), result.Report.Additions[i].Retained);
                Equal(additions[i].GetProperty("reachable_from_commands").GetInt32(), result.Report.Additions[i].ReachableFromCommands);
            }
        });
    }

    private static AinbDocument Graph()
    {
        var root = Node(0, "") with {
            Type = AinbNodeType.Simultaneous, Flags = AinbNodeFlags.Root,
            Properties = new(new[] { new AinbProperty(AinbDataType.Int, "EndPolicy", new AinbInt(0)), new AinbProperty(AinbDataType.Int, "ResultPolicy", new AinbInt(0)) }),
            Plugs = new(new[] { new AinbPlug(AinbPlugType.Child, 1, ""), new AinbPlug(AinbPlugType.Child, 2, "") })
        };
        return new() {
            Name = "Experiment.module", HasSection6C = true,
            Nodes = new(new[] { root, Node(1, "TaskA") with {
                Properties = new(new[] { new AinbProperty(AinbDataType.Float, "Gain", new AinbFloat(1)), new AinbProperty(AinbDataType.Float, "Speed", new AinbFloat(1)) })
            }, Node(2, "TaskB") }),
            Commands = new(new[] { new AinbCommand("Root", Guid.Empty, 0) })
        };
    }
    private static AinbNode Node(int i, string name) => new() { Index = i, Name = name, Id = new Guid(i + 1, 0, 0, new byte[8]) };
    private static AinbDocument Update(AinbDocument d, int index, Func<AinbNode, AinbNode> change) =>
        d with { Nodes = d.Nodes.Select((n, i) => i == index ? change(n) : n).ToAinbList() };
    private static AinbDocument Property(AinbDocument d, int index, float value) => Update(d, 1, n => n with {
        Properties = n.Properties.Select((p, i) => i == index ? p with { Value = new AinbFloat(value) } : p).ToAinbList()
    });
    private static AinbDocument Add(AinbDocument d, string name)
    {
        int index = d.Nodes.Count;
        var result = d with { Nodes = d.Nodes.Append(Node(index, name)).ToAinbList() };
        return Update(result, 0, n => n with { Plugs = n.Plugs.Append(new AinbPlug(AinbPlugType.Child, index, "")).ToAinbList() });
    }
    private static AinbDocument Replace(AinbDocument d, string name)
    {
        var old = d.Nodes[0].Plugs[1]; var added = Add(d, name);
        added = Update(added, added.Nodes.Count - 1, n => n with { Plugs = new(new[] { old }) });
        return Update(added, 0, n => n with { Plugs = new(new[] { n.Plugs[0], n.Plugs[^1] }) });
    }
    private static AinbDocument DeleteB(AinbDocument d) => Update(d with { Nodes = d.Nodes.Take(2).ToAinbList() }, 0,
        n => n with { Plugs = n.Plugs.Take(1).ToAinbList() });
    private static AinbDocument Reorder(AinbDocument d, int[] order)
    {
        var map = order.Select((old, i) => (old, i)).ToDictionary(p => p.old, p => p.i);
        int Ref(int i) => i is -1 or 32767 ? i : map[i];
        return d with {
            Nodes = order.Select(old => d.Nodes[old] with {
                Index = map[old], Id = Guid.Empty, Queries = d.Nodes[old].Queries.Select(Ref).ToAinbList(),
                Plugs = d.Nodes[old].Plugs.Select(p => p with { NodeIndex = Ref(p.NodeIndex) }).ToAinbList(),
                Inputs = d.Nodes[old].Inputs.Select(p => p with {
                    Source = p.Source is null ? null : p.Source with { NodeIndex = Ref(p.Source.NodeIndex) },
                    Sources = p.Sources.Select(s => s with { NodeIndex = Ref(s.NodeIndex) }).ToAinbList()
                }).ToAinbList()
            }).ToAinbList(),
            Commands = d.Commands.Select(c => c with { Id = Guid.Empty, RootNodeIndex = Ref(c.RootNodeIndex),
                SecondaryRootNodeIndex = c.SecondaryRootNodeIndex is int i ? Ref(i) : null }).ToAinbList()
        };
    }
    private static string ChildNames(AinbDocument d) => string.Join(',', d.Nodes[0].Plugs.Where(p => p.Type == AinbPlugType.Child).Select(p => d.Nodes[p.NodeIndex].Name));
    private static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private static void Run(string name, Action action) {
        try { action(); Console.WriteLine($"PASS {name}"); _passed++; }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error}"); _failed++; }
    }
}

// Deliberately not a binary AINB codec: tests isolate merger and adapter behavior
// using owned immutable snapshots. Native binary IO remains Arch's handoff.
internal sealed class SnapshotCodec : IAinbCodec
{
    private readonly Dictionary<int, AinbDocument> _documents = [];
    public bool CorruptWrites { get; init; }
    public int Writes { get; private set; }
    public byte[] Register(AinbDocument d) {
        int id = _documents.FirstOrDefault(p => p.Value == d).Key;
        if (id == 0) { id = _documents.Count + 1; _documents[id] = d; }
        byte[] raw = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(raw, id); return raw;
    }
    public AinbDocument Read(ReadOnlySpan<byte> data) => data.Length == 4 && _documents.TryGetValue(BinaryPrimitives.ReadInt32LittleEndian(data), out var d)
        ? d : throw new InvalidDataException("Unknown test snapshot.");
    public byte[] Write(AinbDocument d) { Writes++; return Register(CorruptWrites ? d with { Name = d.Name + "-corrupt" } : d); }
}
