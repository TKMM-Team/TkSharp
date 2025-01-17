using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using TkSharp.Core.IO.Buffers;
using TkSharp.Merging.Extensions;

namespace TkSharp.Merging.ChangelogBuilders.BinaryYaml;

public class BymlArrayChangelogBuilder : IBymlArrayChangelogBuilder
{
    public static readonly BymlArrayChangelogBuilder Instance = new();
    
    public bool LogChanges(ref BymlTrackingInfo info, ref Byml root, BymlArray src, BymlArray vanilla)
    {
        BymlArrayChangelog changelog = [];
        List<int> editedVanillaIndices = [];
        int detectedAdditions = 0;
        
        using var vanillaRecordsFound = RentedBitArray.Create(vanilla.Count);

        for (int i = 0; i < src.Count; i++) {
            Byml element = src[i];
            if (!vanilla.TryGetIndex(element, Byml.ValueEqualityComparer.Default, out int vanillaIndex)) {
                continue;
            }

            src[i] = BymlChangeType.Remove;
            vanillaRecordsFound[vanillaIndex] = true;
        }

        for (int i = 0; i < vanilla.Count; i++) {
            if (vanillaRecordsFound[i]) {
                continue;
            }

            if (i < src.Count) {
                editedVanillaIndices.Add(i);
                continue;
            }

            changelog.Add((i, BymlChangeType.Remove, new Byml()));
        }

        for (int i = 0; i < src.Count; i++) {
            Byml element = src[i];
            if (element.Type is BymlNodeType.Changelog) {
                continue;
            }

            if (editedVanillaIndices.Count > 0) {
                int vanillaIndex = editedVanillaIndices[0];
                BymlChangelogBuilder.LogChangesInline(ref info, ref element, vanilla[vanillaIndex]);
                changelog.Add((vanillaIndex, BymlChangeType.Edit, element));
                editedVanillaIndices.RemoveAt(0);
                continue;
            }

            changelog.Add((i - detectedAdditions, BymlChangeType.Add, element));
            detectedAdditions++;
        }

        root = changelog;
        return changelog.Count == 0;
    }
}