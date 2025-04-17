using System.Runtime.CompilerServices;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Revrs;
using TkSharp.Core;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class BymlChangelogBuilder : Singleton<BymlChangelogBuilder>, ITkChangelogBuilder
{
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        Byml vanillaByml = Byml.FromBinary(vanillaBuffer);
        Byml srcByml = Byml.FromBinary(srcBuffer, out Endianness endianness, out ushort version);
        BymlTrackingInfo info = new(path.Canonical, depth: 0);
        bool isVanilla = LogChangesInline(ref info, ref srcByml, vanillaByml);

        if (isVanilla) {
            return false;
        }

        using MemoryStream ms = new();
        srcByml.WriteBinary(ms, endianness, version);
        ms.Seek(0, SeekOrigin.Begin);

        using Stream output = openWrite(path, canonical);
        ms.CopyTo(output);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool LogChangesInline(ref BymlTrackingInfo info, ref Byml src, Byml vanilla)
    {
        return LogChangesInline(ref info, ref src, vanilla, BymlArrayChangelogBuilderProvider.Instance);
    }
    
    internal static bool LogChangesInline(ref BymlTrackingInfo info, ref Byml src, Byml vanilla, IBymlArrayChangelogBuilderProvider arrayChangelogBuilderProvider)
    {
        if (src.Type != vanilla.Type) {
            return false;
        }

        return src.Type switch {
            BymlNodeType.HashMap32 => LogMapChanges(ref info, src.GetHashMap32(), vanilla.GetHashMap32(), arrayChangelogBuilderProvider),
            BymlNodeType.HashMap64 => LogMapChanges(ref info, src.GetHashMap64(), vanilla.GetHashMap64(), arrayChangelogBuilderProvider),
            BymlNodeType.Array => info switch {
                { Type: "ecocat", Depth: 0 } => new BymlKeyedArrayChangelogBuilder("AreaNumber")
                    .LogChanges(ref info, ref src, src.GetArray(), vanilla.GetArray()),
                _ => BymlArrayChangelogBuilder.Instance.LogChanges(ref info, ref src, src.GetArray(), vanilla.GetArray())
            },
            BymlNodeType.Map => LogMapChanges(ref info, src.GetMap(), vanilla.GetMap(), arrayChangelogBuilderProvider),
            BymlNodeType.String or
                BymlNodeType.Binary or
                BymlNodeType.BinaryAligned or
                BymlNodeType.Bool or
                BymlNodeType.Int or
                BymlNodeType.Float or
                BymlNodeType.UInt32 or
                BymlNodeType.Int64 or
                BymlNodeType.UInt64 or
                BymlNodeType.Double or
                BymlNodeType.Null => Byml.ValueEqualityComparer.Default.Equals(src, vanilla),
            _ => throw new NotSupportedException(
                $"Merging '{src.Type}' is not supported")
        };
    }
    
    private static bool LogMapChanges<T>(ref BymlTrackingInfo info, IDictionary<T, Byml> src, IDictionary<T, Byml> vanilla, IBymlArrayChangelogBuilderProvider bymlArrayChangelogBuilderProvider) 
    {
        info.Depth++;
        foreach (T key in src.Keys.Concat(vanilla.Keys).Distinct().ToArray()) {
            if (!src.TryGetValue(key, out Byml? srcValue)) {
                src[key] = BymlChangeType.Remove;
                continue;
            }

            if (!vanilla.TryGetValue(key, out Byml? vanillaNode)) {
                continue;
            }

            if (key is string keyName && srcValue.Value is BymlArray array && vanillaNode.Value is BymlArray vanillaArray) {
                if (bymlArrayChangelogBuilderProvider
                    .GetChangelogBuilder(ref info, keyName)
                    .LogChanges(ref info, ref srcValue, array, vanillaArray)) {
                    src.Remove(key);
                    continue;
                }

                goto Default;
            }

            if (LogChangesInline(ref info, ref srcValue, vanillaNode)) {
                src.Remove(key);
                continue;
            }

        Default:
            // CreateChangelog can mutate
            // srcValue, so reassign the key
            src[key] = srcValue;
        }

        info.Depth--;
        return src.Count == 0;
    }
}