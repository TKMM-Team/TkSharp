using System.Security.Cryptography;
using System.Text;
using AinbModel.Contract;

namespace TkSharp.Merging.Mergers.Ainb;

public sealed record AinbGraphResult(AinbDocument Document, AinbMergeReport Report);

/// <summary>Experimental three-way graph merge. Inputs are lowest priority first.</summary>
public static class AinbGraphMerger
{
    public static AinbGraphResult Merge(AinbDocument vanilla, IReadOnlyList<AinbDocument> mods,
        IReadOnlyList<string>? sourceNames = null)
    {
        string[] names = sourceNames?.ToArray() ?? Enumerable.Range(0, mods.Count).Select(i => $"mod-{i}").ToArray();
        if (names.Length != mods.Count) throw new ArgumentException("One name is required per mod.", nameof(sourceNames));
        foreach (var document in mods.Prepend(vanilla)) {
            RequireSupported(document);
            Validate(document);
        }
        foreach (var document in mods) {
            if (document.Version != vanilla.Version || document.Name != vanilla.Name ||
                document.Category != vanilla.Category || document.HasSection6C != vanilla.HasSection6C) {
                throw new AinbMergeNotSupportedException("Changed file identity or shared header section.");
            }
        }

        var original = WithoutGuids(vanilla);
        var originalNodes = original.Nodes.ToDictionary(n => n.Index);
        var current = new Dictionary<int, AinbNode>(originalNodes);
        var baseCommands = original.Commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var commands = new Dictionary<string, AinbCommand>(baseCommands, StringComparer.Ordinal);
        var modules = vanilla.Modules;
        var baseIds = (vanilla.BlackboardId, vanilla.ParentBlackboardId);
        var ids = baseIds;
        AinbMergeReport report = new();
        List<(string Name, int[] Keys)> additions = [];
        // Equal re-exports share an addition namespace, but GUID collisions do not.
        List<(AinbDocument Document, Dictionary<int, int> Map)> snapshots = [];
        int nextKey = vanilla.Nodes.Count;

        for (int ordinal = 0; ordinal < mods.Count; ordinal++) {
            var mod = mods[ordinal];
            string name = names[ordinal];
            ids = Choose(baseIds, ids, (mod.BlackboardId, mod.ParentBlackboardId), "Blackboard IDs", name, report);
            var semanticMod = WithoutGuids(mod);
            if (semanticMod == original) {
                additions.Add((name, []));
                continue;
            }
            var matches = MatchNodes(vanilla, mod, name, report);
            foreach (var (old, incoming) in matches) {
                if (!vanilla.Nodes[old].Outputs.Equals(mod.Nodes[incoming].Outputs))
                    throw new AinbMergeNotSupportedException($"Changed output layout at vanilla node {old}.");
            }
            var known = snapshots.FirstOrDefault(s => s.Document == semanticMod);
            Dictionary<int, int> map;
            if (known.Map is not null) {
                map = known.Map;
            }
            else {
                map = matches.ToDictionary(pair => pair.Value, pair => pair.Key);
                for (int i = 0; i < mod.Nodes.Count; i++) {
                    if (!map.ContainsKey(i)) map[i] = nextKey++;
                }
                snapshots.Add((semanticMod, map));
            }
            additions.Add((name, map.Values.Where(i => i >= vanilla.Nodes.Count).ToArray()));
            var normalized = Remap(semanticMod, map);
            var incomingNodes = normalized.Nodes.ToDictionary(n => n.Index);
            foreach (int key in Keys(originalNodes, current, incomingNodes).ToArray()) {
                if (!originalNodes.ContainsKey(key) && !incomingNodes.ContainsKey(key)) continue;
                var merged = MergeNode(originalNodes.GetValueOrDefault(key), current.GetValueOrDefault(key),
                    incomingNodes.GetValueOrDefault(key), $"Nodes/{key}", name, report);
                if (merged is null) current.Remove(key);
                else current[key] = merged;
            }
            var incomingCommands = normalized.Commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (string key in Keys(baseCommands, commands, incomingCommands).ToArray()) {
                var merged = Choose(baseCommands.GetValueOrDefault(key), commands.GetValueOrDefault(key),
                    incomingCommands.GetValueOrDefault(key), $"Commands/{key}", name, report);
                if (merged is null) commands.Remove(key);
                else commands[key] = merged;
            }
            modules = MergeKeyed(vanilla.Modules, modules, mod.Modules, m => (m.Path, m.Category), "Modules", name, report);
        }

        int[] finalKeys = current.Keys.ToArray();
        var finalMap = finalKeys.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index);
        var output = Remap(original with {
            Nodes = current.Values.ToAinbList(), Commands = commands.Values.ToAinbList(), Modules = modules,
            BlackboardId = ids.Item1, ParentBlackboardId = ids.Item2
        }, finalMap);
        HashSet<Guid> usedIds = [];
        output = output with {
            Nodes = output.Nodes.Select((n, i) => {
                int key = finalKeys[i];
                Guid id = key < vanilla.Nodes.Count ? vanilla.Nodes[key].Id : StableId(vanilla.Name, $"node:{key}");
                for (int salt = 0; !usedIds.Add(id); salt++) id = StableId(vanilla.Name, $"node:{key}:{salt}");
                return n with { Id = id };
            }).ToAinbList(),
            Commands = output.Commands.Select(c => c with {
                Id = vanilla.Commands.FirstOrDefault(old => old.Name == c.Name)?.Id ?? StableId(vanilla.Name, $"command:{c.Name}")
            }).ToAinbList()
        };
        try { Validate(output); }
        catch (InvalidDataException error) { throw new AinbMergeNotSupportedException($"Merged graph is invalid: {error.Message}"); }
        var reachable = Reachable(output);
        report.NodeCount = output.Nodes.Count;
        report.ReachableCount = reachable.Count;
        foreach (var addition in additions) {
            int retained = addition.Keys.Count(finalMap.ContainsKey);
            int live = addition.Keys.Count(key => finalMap.TryGetValue(key, out int index) && reachable.Contains(index));
            report.Additions.Add(new(addition.Name, retained, live));
            if (retained != live) report.Warnings.Add($"{addition.Name}: {retained - live} added nodes are not reachable from commands.");
        }
        if (mods.Any(m => (m.BlackboardId, m.ParentBlackboardId) != baseIds))
            report.Warnings.Add("Empty-blackboard IDs use atomic priority; cross-file runtime compatibility is unverified.");
        return new(output, report);
    }

    public static AinbDocument WithoutGuids(AinbDocument document) => document with {
        Nodes = document.Nodes.Select(n => n with { Id = Guid.Empty }).ToAinbList(),
        Commands = document.Commands.Select(c => c with { Id = Guid.Empty }).ToAinbList()
    };

    private static Dictionary<int, int> MatchNodes(AinbDocument vanilla, AinbDocument mod, string source, AinbMergeReport report)
    {
        Dictionary<int, int> matches = [];
        void Match(int old, int incoming, string reason) {
            if (matches.TryGetValue(old, out int prior)) {
                if (prior != incoming) throw new AinbMergeNotSupportedException($"Conflicting anchor for vanilla node {old}.");
                return;
            }
            if (matches.ContainsValue(incoming) || Role(vanilla.Nodes[old]) != Role(mod.Nodes[incoming]))
                throw new AinbMergeNotSupportedException($"Ambiguous or changed node role at vanilla node {old}.");
            matches.Add(old, incoming);
            report.Matches.Add(new(source, old, incoming, reason));
        }
        var commands = mod.Commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var command in vanilla.Commands.OrderBy(c => c.Name, StringComparer.Ordinal)) {
            if (!commands.TryGetValue(command.Name, out var incoming)) continue;
            Match(command.RootNodeIndex, incoming.RootNodeIndex, $"Command {command.Name}/Root");
            if (command.SecondaryRootNodeIndex is int old && incoming.SecondaryRootNodeIndex is int next)
                Match(old, next, $"Command {command.Name}/SecondaryRoot");
        }
        void Unique<TKey>(Func<AinbNode, TKey> identity, string reason) where TKey : notnull {
            var originals = vanilla.Nodes.Where(n => !matches.ContainsKey(n.Index)).GroupBy(identity).ToDictionary(g => g.Key, g => g.ToArray());
            var incoming = mod.Nodes.Where(n => !matches.ContainsValue(n.Index)).GroupBy(identity).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var (key, old) in originals) {
                if (old.Length == 1 && incoming.TryGetValue(key, out var next) && next.Length == 1)
                    Match(old[0].Index, next[0].Index, reason);
            }
        }
        Unique(LocalShape, "Unique local contents");
        Unique(Role, "Unique type/name/module role");
        foreach (var node in vanilla.Nodes) {
            if (!matches.ContainsKey(node.Index) && mod.Nodes.Any(n => Role(n) == Role(node)))
                throw new AinbMergeNotSupportedException($"Ambiguous or replaced vanilla node {node.Index}.");
        }
        return matches;
    }

    private static (AinbNodeType, string, bool) Role(AinbNode n) => (n.Type, n.Name, n.Flags.HasFlag(AinbNodeFlags.Module));
    private static AinbNode LocalShape(AinbNode n) => n with {
        Index = 0, Id = Guid.Empty, Queries = new(), Plugs = new(),
        Inputs = n.Inputs.Select(p => p with {
            Source = p.Source is null ? null : p.Source with { NodeIndex = -1, OutputIndex = 0 }, Sources = new()
        }).ToAinbList()
    };

    private static AinbNode? MergeNode(AinbNode? old, AinbNode? low, AinbNode? high, string path, string source, AinbMergeReport report)
    {
        if (old is null || low is null || high is null) return Choose(old, low, high, path, source, report);
        if (high == old || high == low) return low;
        if (low == old) return high;
        List<AinbPlug> plugs = [];
        foreach (var type in old.Plugs.Concat(low.Plugs).Concat(high.Plugs).Select(p => p.Type).Distinct()) {
            var a = old.Plugs.Where(p => p.Type == type).ToAinbList();
            var b = low.Plugs.Where(p => p.Type == type).ToAinbList();
            var c = high.Plugs.Where(p => p.Type == type).ToAinbList();
            AinbList<AinbPlug>? children = null;
            if (type == AinbPlugType.Child && old.Type == AinbNodeType.Simultaneous &&
                old.Properties.Equals(low.Properties) && old.Properties.Equals(high.Properties)) {
                children = MergeChildren(a, b, c, $"{path}/Plugs/Child", source, report);
            }
            plugs.AddRange(children ?? Choose(a, b, c, $"{path}/Plugs/{type}", source, report));
        }
        return old with {
            Type = Choose(old.Type, low.Type, high.Type, $"{path}/Type", source, report),
            Name = Choose(old.Name, low.Name, high.Name, $"{path}/Name", source, report),
            Flags = Choose(old.Flags, low.Flags, high.Flags, $"{path}/Flags", source, report),
            Queries = Choose(old.Queries, low.Queries, high.Queries, $"{path}/Queries", source, report),
            Properties = MergeParameters(old.Properties, low.Properties, high.Properties, p => p.Type, p => p.Name, $"{path}/Properties", source, report),
            Inputs = MergeParameters(old.Inputs, low.Inputs, high.Inputs, p => p.Type, p => p.Name, $"{path}/Inputs", source, report),
            Outputs = old.Outputs, Plugs = plugs.ToAinbList()
        };
    }

    private static AinbList<AinbPlug>? MergeChildren(AinbList<AinbPlug> old, AinbList<AinbPlug> low,
        AinbList<AinbPlug> high, string path, string source, AinbMergeReport report)
    {
        var targets = old.Select(p => p.NodeIndex).ToHashSet();
        if (targets.Count != old.Count) return null;
        bool Supported(AinbList<AinbPlug> list) {
            if (list.Count < old.Count || list.Select(p => p.NodeIndex).Distinct().Count() != list.Count) return false;
            for (int i = 0; i < old.Count; i++) {
                if (list[i] == old[i]) continue;
                if (targets.Contains(list[i].NodeIndex) || list[i] with { NodeIndex = old[i].NodeIndex } != old[i]) return false;
            }
            return !list.Skip(old.Count).Any(p => targets.Contains(p.NodeIndex));
        }
        if (!Supported(low) || !Supported(high)) return null;
        // Stage conflicts until the combined list passes the duplicate-target guard.
        AinbMergeReport pending = new();
        var result = old.Select((p, i) => Choose(p, low[i], high[i], $"{path}/{i}", source, pending)).ToList();
        result.AddRange(MergeKeyed([], low.Skip(old.Count), high.Skip(old.Count), p => p.NodeIndex, $"{path}/Appended", source, pending));
        if (result.Select(p => p.NodeIndex).Distinct().Count() != result.Count) return null;
        report.Conflicts.AddRange(pending.Conflicts);
        return result.ToAinbList();
    }

    private static T Choose<T>(T old, T low, T high, string path, string source, AinbMergeReport report)
    {
        var eq = EqualityComparer<T>.Default;
        if (eq.Equals(high, old) || eq.Equals(high, low)) return low;
        if (eq.Equals(low, old)) return high;
        report.Conflicts.Add(new(source, path, old is null || low is null || high is null ? "delete/edit" : "edit/edit"));
        return high;
    }

    private static AinbList<T> MergeParameters<T>(IEnumerable<T> old, IEnumerable<T> low, IEnumerable<T> high,
        Func<T, AinbDataType> type, Func<T, string> name, string path, string source, AinbMergeReport report) where T : class =>
        old.Concat(low).Concat(high).Select(type).Distinct().SelectMany(t =>
            MergeKeyed(old.Where(p => type(p) == t), low.Where(p => type(p) == t), high.Where(p => type(p) == t),
                name, $"{path}/{t}", source, report)).ToAinbList();

    private static AinbList<T> MergeKeyed<T, TKey>(IEnumerable<T> old, IEnumerable<T> low, IEnumerable<T> high,
        Func<T, TKey> key, string path, string source, AinbMergeReport report) where T : class where TKey : notnull
    {
        Dictionary<TKey, T> Map(IEnumerable<T> entries) {
            Dictionary<TKey, T> result = [];
            foreach (var entry in entries) {
                if (!result.TryAdd(key(entry), entry)) throw new AinbMergeNotSupportedException($"Duplicate key in {path}.");
            }
            return result;
        }
        var a = Map(old); var b = Map(low); var c = Map(high);
        List<T> merged = [];
        foreach (var id in Keys(a, b, c)) {
            var value = Choose(a.GetValueOrDefault(id), b.GetValueOrDefault(id), c.GetValueOrDefault(id), $"{path}/{id}", source, report);
            if (value is not null) merged.Add(value);
        }
        return merged.ToAinbList();
    }

    private static IEnumerable<TKey> Keys<TKey, T>(Dictionary<TKey, T> a, Dictionary<TKey, T> b, Dictionary<TKey, T> c)
        where TKey : notnull => a.Keys.Concat(b.Keys).Concat(c.Keys).Distinct();

    private static AinbDocument Remap(AinbDocument document, IReadOnlyDictionary<int, int> map)
    {
        int Ref(int index) => index is -1 or 32767 ? index : map.TryGetValue(index, out int next) ? next :
            throw new AinbMergeNotSupportedException($"Unresolved node reference: {index}.");
        AinbSource Source(AinbSource s) => s with { NodeIndex = Ref(s.NodeIndex) };
        return document with {
            Nodes = document.Nodes.Select(n => n with {
                Index = Ref(n.Index), Queries = n.Queries.Select(Ref).ToAinbList(),
                Inputs = n.Inputs.Select(p => p with {
                    Source = p.Source is null ? null : Source(p.Source), Sources = p.Sources.Select(Source).ToAinbList()
                }).ToAinbList(),
                Plugs = n.Plugs.Select(p => p with { NodeIndex = Ref(p.NodeIndex) }).ToAinbList()
            }).ToAinbList(),
            Commands = document.Commands.Select(c => c with {
                RootNodeIndex = Ref(c.RootNodeIndex), SecondaryRootNodeIndex = c.SecondaryRootNodeIndex is int i ? Ref(i) : null
            }).ToAinbList()
        };
    }

    public static void RequireSupported(AinbDocument document)
    {
        if (document.Version != 0x407 || document.UnsupportedFeatures != AinbUnsupportedFeatures.None)
            throw new AinbMergeNotSupportedException($"Unsupported AINB version/features: {document.Version:X}/{document.UnsupportedFeatures}.");
        if (document.Nodes.Any(n => !Enum.IsDefined(n.Type) || (n.Flags & ~(AinbNodeFlags.Query | AinbNodeFlags.Module | AinbNodeFlags.Root)) != 0))
            throw new AinbMergeNotSupportedException("Unknown node type or flags.");
        const AinbParameterFlags known = AinbParameterFlags.UsesDefault | AinbParameterFlags.IsOutput;
        foreach (var node in document.Nodes) {
            if (node.Plugs.Any(p => !Enum.IsDefined(p.Type)) ||
                node.Properties.Any(p => !Enum.IsDefined(p.Type) || (p.Flags & ~known) != 0) ||
                node.Outputs.Any(p => !Enum.IsDefined(p.Type)) ||
                node.Inputs.Any(p => !Enum.IsDefined(p.Type) || (p.Flags & ~known) != 0 ||
                    (p.Source is { } s && (s.Flags & ~known) != 0) || p.Sources.Any(s => (s.Flags & ~known) != 0)))
                throw new AinbMergeNotSupportedException("Unrepresented plug, parameter type or flags.");
        }
    }

    public static void Validate(AinbDocument document)
    {
        if (document.Nodes.Count > 32767) throw new InvalidDataException("Too many nodes for signed node references.");
        void Ref(int i, bool nullable = false) {
            if (nullable && i is -1 or 32767) return;
            if ((uint)i >= document.Nodes.Count) throw new InvalidDataException($"Invalid node reference {i}.");
        }
        void Value(AinbDataType type, AinbValue value) {
            bool valid = (type, value) switch {
                (AinbDataType.Int, AinbInt) or (AinbDataType.Bool, AinbBool) or
                (AinbDataType.String, AinbString) or (AinbDataType.Pointer, AinbNullPointer) => true,
                (AinbDataType.Float, AinbFloat f) => float.IsFinite(f.Value),
                (AinbDataType.Vector3F, AinbVector v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z),
                _ => false
            };
            if (!valid) throw new InvalidDataException("Parameter value does not match its declared type or is non-finite.");
        }
        var commandNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in document.Commands) {
            if (!commandNames.Add(c.Name)) throw new InvalidDataException($"Duplicate command {c.Name}.");
            Ref(c.RootNodeIndex);
            if (c.SecondaryRootNodeIndex is int i) Ref(i);
        }
        foreach (var (n, index) in document.Nodes.Select((n, i) => (n, i))) {
            if (n.Index != index) throw new InvalidDataException("Stored node index disagrees with array order.");
            if (n.Flags.HasFlag(AinbNodeFlags.Module) && !document.Modules.Any(m => m.Path == n.Name + ".ainb"))
                throw new InvalidDataException($"Missing module declaration {n.Name}.");
            foreach (int query in n.Queries) {
                Ref(query);
                if (!document.Nodes[query].Flags.HasFlag(AinbNodeFlags.Query)) throw new InvalidDataException("Query target is not a query node.");
            }
            foreach (var plug in n.Plugs) Ref(plug.NodeIndex, true);
            foreach (var property in n.Properties) Value(property.Type, property.Value);
            foreach (var input in n.Inputs) {
                Value(input.Type, input.Value);
                if ((input.Source is null) == (input.Sources.Count == 0)) throw new InvalidDataException("Expected either a direct source or multiple sources.");
                foreach (var source in input.Source is { } single ? new[] { single } : input.Sources.ToArray()) {
                    Ref(source.NodeIndex, true);
                    if (source.NodeIndex is -1 or 32767) continue;
                    int count = document.Nodes[source.NodeIndex].Outputs.Count(p => p.Type == input.Type);
                    if ((uint)source.OutputIndex >= count) throw new InvalidDataException("Input points outside its source's typed output table.");
                }
            }
        }
    }

    public static HashSet<int> Reachable(AinbDocument document)
    {
        HashSet<int> visited = [];
        Stack<int> pending = new(document.Commands.SelectMany(c => c.SecondaryRootNodeIndex is int i ? new[] { c.RootNodeIndex, i } : [c.RootNodeIndex]));
        while (pending.TryPop(out int index)) {
            if (!visited.Add(index)) continue;
            var n = document.Nodes[index];
            var refs = n.Queries.Concat(n.Plugs.Select(p => p.NodeIndex)).Concat(n.Inputs.SelectMany(p =>
                p.Source is { } single ? new[] { single.NodeIndex } : p.Sources.Select(s => s.NodeIndex)));
            foreach (int target in refs) if ((uint)target < document.Nodes.Count) pending.Push(target);
        }
        return visited;
    }

    private static Guid StableId(string filename, string identity)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(filename + "\0" + identity));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80); // UUIDv8: implementation-defined deterministic payload.
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }
}
