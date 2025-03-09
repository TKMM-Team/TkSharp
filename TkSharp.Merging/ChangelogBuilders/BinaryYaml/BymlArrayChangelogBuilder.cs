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
        Queue<int> additions = [];

        using var vanillaRecordsFound = RentedBitArray.Create(vanilla.Count);
        using var srcRecordsThatAreVanilla = RentedBitArray.Create(src.Count);

        for (int i = 0; i < src.Count; i++) {
            Byml element = src[i];
            if (!vanilla.TryGetIndex(element, Byml.ValueEqualityComparer.Default, out int vanillaIndex)) {
                additions.Enqueue(i);
                continue;
            }

            // src[i] = BymlChangeType.Remove;
            srcRecordsThatAreVanilla[i] = true;
            vanillaRecordsFound[vanillaIndex] = true;
        }

        for (int i = 0; i < vanilla.Count; i++) {
            // This vanilla entry has a 1:1 pair in
            // the modified array, so ignore this step
            if (vanillaRecordsFound[i]) {
                continue;
            }

            // If the modded value at this index is
            // vanilla, remove the vanilla index
            if (i < src.Count && srcRecordsThatAreVanilla[i] is false) {
                // If we are still in the bounds of the
                // modded array, but the value is not vanilla,
                // consider this index modified
                Byml element = src[i];
                BymlChangelogBuilder.LogChangesInline(ref info, ref element, vanilla[i]);
                changelog.Add(
                    (index: i, BymlChangeType.Edit, node: element)
                );
                additions.Dequeue();
                continue;
            }

            changelog.Add(
                (index: i, BymlChangeType.Remove, node: new Byml())
            );
        }

        if (additions.TryPeek(out int index)) {
            for (int i = 0; i < additions.Count; i++) {
                changelog.Add(
                    (index, BymlChangeType.Add, node: src[index])
                );
            }
        }

        root = changelog;
        return changelog.Count == 0;
    }
}