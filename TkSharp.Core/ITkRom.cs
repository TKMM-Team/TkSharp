using System.Runtime.CompilerServices;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Core;

public interface ITkRom : IDisposable
{
    private const string AI_FOLDER = "AI/";
    private const string EVENT_FLOW_FOLDER = "Event/EventFlow";
    private const string EFFECT_FOLDER = "Effect";
    private const string LOGIC_FOLDER = "Logic/";
    private const string SEQUENCE_FOLDER = "Sequence/";

    int GameVersion { get; }

    string NsoBinaryId { get; }

    TkZstd Zstd { get; }

    IDictionary<string, string> AddressTable { get; }

    Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions { get; }

    Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EffectVersions { get; }

    Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> AiVersions { get; }

    Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> LogicVersions { get; }

    Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> SequenceVersions { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string CanonicalToRelativePath(string canonical, TkFileAttributes attributes)
    {
        string result = AddressTable.TryGetValue(canonical, out string? address)
            ? address
            : canonical;

        var canon = result.AsSpan();

        if (canon.Length > 3 && canon[..3] is AI_FOLDER && AiVersions.TryGetValue(canon, out string? aiVersionFile)) {
            result = aiVersionFile;
            goto ProcessFlags;
        }
        
        if (canon.Length > 26 && canon[..15] is EVENT_FLOW_FOLDER && canon[16..^7] is var eventFlowName
            && EventFlowVersions.TryGetValue(eventFlowName, out string? version)) {
            result = $"{EVENT_FLOW_FOLDER}/{eventFlowName}.{version}{Path.GetExtension(canon)}";
            goto ProcessFlags;
        }

        if (attributes.HasFlag(TkFileAttributes.IsProductFile) && canon.Length > 37 && canon[..6] is EFFECT_FOLDER
            && canon[7..^30] is var effectName && EffectVersions.TryGetValue(effectName, out string? effectFileName)) {
            result = $"{EFFECT_FOLDER}/{effectFileName}.Product.Nin_NX_NVN.esetb.byml";
            goto ProcessFlags;
        }

        if (canon.Length > 9 && canon[..9] is SEQUENCE_FOLDER && SequenceVersions.TryGetValue(canon, out string? sequenceVersionFile)) {
            result = sequenceVersionFile;
            goto ProcessFlags;
        }

        if (canon.Length > 6 && canon[..6] is LOGIC_FOLDER && LogicVersions.TryGetValue(canon, out string? logicVersionFile)) {
            result = logicVersionFile;
        }

    ProcessFlags:
        if (attributes.HasFlag(TkFileAttributes.HasZsExtension)) {
            result += ".zs";
        }

        if (attributes.HasFlag(TkFileAttributes.HasMcExtension)) {
            result += ".mc";
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RentedBuffer<byte> GetVanilla(string canonical, TkFileAttributes attributes)
        => GetVanilla(CanonicalToRelativePath(canonical, attributes), out _);

    RentedBuffer<byte> GetVanilla(string relativeFilePath)
        => GetVanilla(relativeFilePath, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RentedBuffer<byte> GetVanilla(string canonical, TkFileAttributes attributes, out bool isFoundMissing)
        => GetVanilla(CanonicalToRelativePath(canonical, attributes), out isFoundMissing);

    RentedBuffer<byte> GetVanilla(string relativeFilePath, out bool isFoundMissing);

    bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion);
}