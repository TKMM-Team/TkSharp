using System.Text.Json;
using AinbFormat;

namespace AinbMerge.Tests;

// Test-only translation of dt's JSON. This is not a codec and is never used by TkSharp.
internal static class FixtureJson
{
    public static AinbDocument Read(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var d = json.RootElement;
        CheckKeys(d, "Version", "Filename", "Category", "Blackboard ID", "Parent Blackboard ID", "Blackboard",
            "Expressions", "Replacement Table", "Unknown Section 0x58", "Has Section 0x6C", "Nodes", "Commands", "Modules");
        foreach (string field in new[] { "Blackboard", "Expressions", "Unknown Section 0x58" })
            if (d.GetProperty(field).EnumerateObject().Any()) throw new InvalidDataException($"Test translator does not implement {field}.");
        if (d.GetProperty("Replacement Table").GetArrayLength() != 0) throw new InvalidDataException("Replacement table in test fixture.");
        return new()
        {
            Version = d.GetProperty("Version").GetUInt32(),
            Name = Text(d, "Filename"),
            Category = Text(d, "Category"),
            BlackboardId = d.GetProperty("Blackboard ID").GetUInt32(),
            ParentBlackboardId = d.GetProperty("Parent Blackboard ID").GetUInt32(),
            HasSection6C = d.GetProperty("Has Section 0x6C").GetBoolean(),
            Nodes = d.GetProperty("Nodes").EnumerateArray().Select(Node).ToAinbList(),
            Commands = d.GetProperty("Commands").EnumerateArray().Select(c =>
            {
                CheckKeys(c, "Name", "GUID", "Root Node Index", "Secondary Root Node Index");
                return new AinbCommand(Text(c, "Name"), Guid.Parse(Text(c, "GUID")), c.GetProperty("Root Node Index").GetInt32(),
                    c.TryGetProperty("Secondary Root Node Index", out var secondary) ? secondary.GetInt32() : null);
            }).ToAinbList(),
            Modules = d.GetProperty("Modules").EnumerateArray().Select(m =>
            {
                CheckKeys(m, "Path", "Category", "Instance Count");
                return new AinbModule(Text(m, "Path"), Text(m, "Category"), m.GetProperty("Instance Count").GetUInt32());
            }).ToAinbList()
        };
    }

    private static AinbNode Node(JsonElement n)
    {
        CheckKeys(n, "Node Type", "Node Index", "Name", "GUID", "Flags", "Queries", "Attachments", "Properties", "Parameters", "XLink Actions", "Plugs");
        if (n.GetProperty("Attachments").GetArrayLength() != 0 || n.GetProperty("XLink Actions").GetArrayLength() != 0)
            throw new InvalidDataException("Test translator does not implement attachments or XLink actions.");
        var parameters = n.GetProperty("Parameters");
        CheckKeys(parameters, "Inputs", "Outputs");
        string nodeType = Text(n, "Node Type").Replace("Element_", "", StringComparison.Ordinal);
        AinbNodeFlags flags = 0;
        foreach (var flag in n.GetProperty("Flags").EnumerateArray()) flags |= flag.GetString() switch
        {
            "Is Query" => AinbNodeFlags.Query,
            "Is Module" => AinbNodeFlags.Module,
            "Is Root Node" => AinbNodeFlags.Root,
            _ => throw new InvalidDataException("Unknown node flag in test translator.")
        };
        return new()
        {
            Type = Enum.Parse<AinbNodeType>(nodeType),
            Index = n.GetProperty("Node Index").GetInt32(),
            Name = Text(n, "Name"),
            Id = Guid.Parse(Text(n, "GUID")),
            Flags = flags,
            Queries = n.GetProperty("Queries").EnumerateArray().Select(v => v.GetInt32()).ToAinbList(),
            Properties = Typed(n.GetProperty("Properties"), (type, p) =>
            {
                CheckKeys(p, "Name", "Default Value", "Flags");
                return new AinbProperty(type, Text(p, "Name"), Value(type, p.GetProperty("Default Value")), Flags(p));
            }),
            Inputs = Typed(parameters.GetProperty("Inputs"), (type, p) =>
            {
                CheckKeys(p, "Name", "Default Value", "Flags", "Classname", "Is Set Blackboard", "Node Index", "Output Index", "Sources");
                return new AinbInput(type, Text(p, "Name"), Value(type, p.GetProperty("Default Value")), Flags(p),
                    p.TryGetProperty("Classname", out var cls) ? cls.GetString() : null,
                    p.TryGetProperty("Is Set Blackboard", out var set) ? set.GetBoolean() : null,
                    p.TryGetProperty("Sources", out var sources) ? null : new AinbSource(p.GetProperty("Node Index").GetInt32(), p.GetProperty("Output Index").GetInt32()),
                    sources.ValueKind == JsonValueKind.Array ? sources.EnumerateArray().Select(Source).ToAinbList() : new());
            }),
            Outputs = Typed(parameters.GetProperty("Outputs"), (type, p) =>
            {
                CheckKeys(p, "Name", "Is Output", "Classname");
                return new AinbOutput(type, Text(p, "Name"), p.GetProperty("Is Output").GetBoolean(),
                    p.TryGetProperty("Classname", out var cls) ? cls.GetString() : null);
            }),
            Plugs = n.GetProperty("Plugs").EnumerateObject().SelectMany(group => group.Value.EnumerateArray().Select(p =>
            {
                CheckKeys(p, "Name", "Node Index", "Unknown 1", "Unknown 2");
                return new AinbPlug(Enum.Parse<AinbPlugType>(group.Name), p.GetProperty("Node Index").GetInt32(), Text(p, "Name"),
                    p.TryGetProperty("Unknown 1", out var u1) ? u1.GetUInt32() : null,
                    p.TryGetProperty("Unknown 2", out var u2) ? u2.GetUInt32() : null);
            })).ToAinbList()
        };
    }

    private static AinbSource Source(JsonElement p)
    {
        CheckKeys(p, "Node Index", "Output Index", "Flags");
        return new(p.GetProperty("Node Index").GetInt32(), p.GetProperty("Output Index").GetInt32(), Flags(p));
    }
    private static AinbList<T> Typed<T>(JsonElement groups, Func<AinbDataType, JsonElement, T> read) =>
        groups.EnumerateObject().SelectMany(g => g.Value.EnumerateArray().Select(p => read(Enum.Parse<AinbDataType>(g.Name), p))).ToAinbList();
    private static AinbParameterFlags Flags(JsonElement p)
    {
        AinbParameterFlags flags = 0;
        foreach (var flag in p.GetProperty("Flags").EnumerateArray()) flags |= flag.GetString() switch
        {
            "Uses Default" => AinbParameterFlags.UsesDefault,
            "Is Output" => AinbParameterFlags.IsOutput,
            _ => throw new InvalidDataException("Unknown parameter flag in test translator.")
        };
        return flags;
    }
    private static AinbValue Value(AinbDataType type, JsonElement v) => type switch
    {
        AinbDataType.Int => new AinbInt(v.GetInt32()),
        AinbDataType.Bool => new AinbBool(v.GetBoolean()),
        AinbDataType.Float => new AinbFloat(v.GetSingle()),
        AinbDataType.String => new AinbString(v.GetString()!),
        AinbDataType.Vector3F => new AinbVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()),
        AinbDataType.Pointer when v.ValueKind == JsonValueKind.Null => new AinbNullPointer(),
        _ => throw new InvalidDataException("Unrepresented parameter value in test fixture.")
    };
    private static string Text(JsonElement d, string key) => d.GetProperty(key).GetString()!;
    private static void CheckKeys(JsonElement d, params string[] keys)
    {
        foreach (var p in d.EnumerateObject())
            if (!keys.Contains(p.Name, StringComparer.Ordinal)) throw new InvalidDataException($"Unrepresented test field: {p.Name}");
    }
}
