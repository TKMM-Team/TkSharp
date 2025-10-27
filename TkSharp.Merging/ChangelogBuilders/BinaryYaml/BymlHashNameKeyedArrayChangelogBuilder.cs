using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Merging.ChangelogBuilders.BinaryYaml;

public class BymlNameHashArrayChangelogBuilder : Singleton<BymlNameHashArrayChangelogBuilder>, IBymlArrayChangelogBuilder
{
    private const string KEY = "NameHash";

    public bool LogChanges(ref BymlTrackingInfo info, ref Byml root, BymlArray src, BymlArray vanilla)
    {
        BymlArrayChangelog changelog = [];
        using var vanillaRecordsFound = RentedBitArray.Create(vanilla.Count);

        for (var i = 0; i < src.Count; i++) {
            var node = src[i];

            int vanillaIndex;
            Byml? keyPrimary;
            
            switch (node.GetMap()[KEY].Value) {
                case uint u32:
                    if (!TryGetIndex(vanilla, u32, out vanillaIndex)) {
                        changelog.Add((i, BymlChangeType.Add, node));
                        continue;
                    }

                    keyPrimary = u32;
                    break;
                case int s32:
                    if (!TryGetIndex(vanilla, s32, out vanillaIndex)) {
                        changelog.Add((i, BymlChangeType.Add, node));
                        continue;
                    }
                    
                    keyPrimary = s32;
                    break;
                default:
                    throw new InvalidOperationException("Invalid NameHash key type.");
            };
            

            if (BymlChangelogBuilder.LogChangesInline(ref info, ref node, vanilla[vanillaIndex])) {
                src[i] = BymlChangeType.Remove;
                goto UpdateVanilla;
            }

            changelog.Add((vanillaIndex, BymlChangeType.Edit, node, keyPrimary, keySecondary: null));

        UpdateVanilla:
            vanillaRecordsFound[i] = true;
        }

        for (var i = 0; i < vanilla.Count; i++) {
            if (vanillaRecordsFound[i]) {
                continue;
            }

            changelog.Add((i, BymlChangeType.Remove, new Byml()));
        }

        root = changelog;
        return changelog.Count == 0;
    }

    private static bool TryGetIndex<T>(BymlArray list, T element, out int index) where T : IEquatable<T>
    {
        var len = list.Count;
        for (var i = 0; i < len; i++) {
            if (list[i].Value is not BymlMap map || !map[KEY].Value?.Equals(element) is true) {
                continue;
            }

            index = i;
            return true;
        }

        index = -1;
        return false;
    }
}