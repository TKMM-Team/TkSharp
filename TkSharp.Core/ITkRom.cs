using System.Runtime.CompilerServices;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Core;

public interface ITkRom : IDisposable
{
    private const string EVENT_FLOW_FOLDER = "Event/EventFlow";
    private const string EFFECT_FOLDER = "Effect";
    private const string AI_FOLDER = "AI";
    private const string LOGIC_FOLDER = "Logic";
    private const string SEQUENCE_FOLDER = "Sequence";

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

        ReadOnlySpan<char> canon = result.AsSpan();
        if (canon.Length > 26 && canon[..15] is EVENT_FLOW_FOLDER && canon[16..^7] is var eventFlowName
            && EventFlowVersions.TryGetValue(eventFlowName, out string? version)) {
            result = $"{EVENT_FLOW_FOLDER}/{eventFlowName}.{version}{Path.GetExtension(canon)}";
        }

        if (attributes.HasFlag(TkFileAttributes.IsProductFile) && canon.Length > 37 && canon[..6] is EFFECT_FOLDER
            && canon[7..^30] is var effectName && EffectVersions.TryGetValue(effectName, out string? effectFileName)) {
            result = $"{EFFECT_FOLDER}/{effectFileName}.Product.Nin_NX_NVN.esetb.byml";
        }

        // TODO: Implement AI, Logic, and Sequence versions
        // not sure how to do these...

        //if (canon[..2] is AI_FOLDER && canon[16..^7] is var aiName
        //    && AIVersions.TryGetValue(aiName, out string? version)) {
        //    result = $"{AI_FOLDER}/{aiName}.{version}{Path.GetExtension(canon)}";
        //}

        //if (canon.Length > 26 && canon[..15] is LOGIC_FOLDER && canon[16..^7] is var logicName
        //    && LogicVersions.TryGetValue(logicName, out string? version)) {
        //    result = $"{LOGIC_FOLDER}/{logicName}.{version}{Path.GetExtension(canon)}";
        //}

        //if (canon.Length > 26 && canon[..15] is SEQUENCE_FOLDER && canon[16..^7] is var sequenceName
        //    && SequenceVersions.TryGetValue(sequenceName, out string? version)) {
        //    result = $"{SEQUENCE_FOLDER}/{sequenceName}.{version}.module.{Path.GetExtension(canon)}";
        //}

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
    {
        return GetVanilla(
            CanonicalToRelativePath(canonical, attributes)
        );
    }

    RentedBuffer<byte> GetVanilla(string relativeFilePath);

    bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion);
}