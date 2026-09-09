namespace AinbModel.Contract;

// This is a proposed interchange contract for Arch, not the published AinbLibrary API.
public enum AinbNodeType : ushort
{
    UserDefined = 0, S32Selector = 1, Sequential = 2, Simultaneous = 3,
    F32Selector = 4, StringSelector = 5, RandomSelector = 6, BoolSelector = 7,
    Fork = 8, Join = 9, Alert = 10, Expression = 20,
    ModuleInputS32 = 100, ModuleInputF32 = 101, ModuleInputVector = 102,
    ModuleInputString = 103, ModuleInputBool = 104, ModuleInputPointer = 105,
    ModuleOutputS32 = 200, ModuleOutputF32 = 201, ModuleOutputVector = 202,
    ModuleOutputString = 203, ModuleOutputBool = 204, ModuleOutputPointer = 205,
    ModuleChild = 300, StateEnd = 400, SplitTiming = 500
}

[Flags]
public enum AinbNodeFlags { None = 0, Query = 1, Module = 2, Root = 4 }
public enum AinbDataType { Int, Bool, Float, String, Vector3F, Pointer }
public enum AinbPlugType { Child, Generic, Int, String }

// Semantic flags, not masks to write directly to the binary format.
[Flags]
public enum AinbParameterFlags { None = 0, UsesDefault = 1, IsOutput = 2 }

[Flags]
public enum AinbUnsupportedFeatures
{
    None = 0, Blackboard = 1, Expressions = 2, Replacements = 4,
    UnknownSection58 = 8, Attachments = 16, XLinkActions = 32,
    ExtendedParameters = 64, AdvancedPlugs = 128, UnknownNodeFlags = 256,
    UnknownData = 512
}

public abstract record AinbValue;
public sealed record AinbInt(int Value) : AinbValue;
public sealed record AinbBool(bool Value) : AinbValue;
public sealed record AinbFloat(float Value) : AinbValue;
public sealed record AinbString(string Value) : AinbValue;
public sealed record AinbVector(float X, float Y, float Z) : AinbValue;
public sealed record AinbNullPointer : AinbValue;

public sealed record AinbProperty(AinbDataType Type, string Name, AinbValue Value,
    AinbParameterFlags Flags = AinbParameterFlags.None);
public sealed record AinbSource(int NodeIndex, int OutputIndex,
    AinbParameterFlags Flags = AinbParameterFlags.None);
public sealed record AinbInput(AinbDataType Type, string Name, AinbValue Value,
    AinbParameterFlags Flags, string? ClassName, bool? IsSetBlackboard,
    AinbSource? Source, AinbList<AinbSource> Sources);
public sealed record AinbOutput(AinbDataType Type, string Name, bool IsOutput, string? ClassName);
public sealed record AinbPlug(AinbPlugType Type, int NodeIndex, string Name,
    uint? Unknown1 = null, uint? Unknown2 = null);
public sealed record AinbCommand(string Name, Guid Id, int RootNodeIndex, int? SecondaryRootNodeIndex = null);
public sealed record AinbModule(string Path, string Category, uint InstanceCount);

public sealed record AinbNode
{
    public int Index { get; init; }
    public Guid Id { get; init; }
    public AinbNodeType Type { get; init; }
    public string Name { get; init; } = "";
    public AinbNodeFlags Flags { get; init; }
    public AinbList<int> Queries { get; init; } = new();
    public AinbList<AinbProperty> Properties { get; init; } = new();
    public AinbList<AinbInput> Inputs { get; init; } = new();
    public AinbList<AinbOutput> Outputs { get; init; } = new();
    public AinbList<AinbPlug> Plugs { get; init; } = new();
}

public sealed record AinbDocument
{
    public uint Version { get; init; } = 0x407;
    public string Name { get; init; } = "";
    public string Category { get; init; } = "AI";
    public uint BlackboardId { get; init; }
    public uint ParentBlackboardId { get; init; }
    public bool HasSection6C { get; init; }
    public AinbUnsupportedFeatures UnsupportedFeatures { get; init; }
    public AinbList<AinbNode> Nodes { get; init; } = new();
    public AinbList<AinbCommand> Commands { get; init; } = new();
    public AinbList<AinbModule> Modules { get; init; } = new();
}
