using BymlLibrary;
using BymlLibrary.Nodes.Containers;

namespace TkSharp.Merging.ChangelogBuilders.BinaryYaml;

public class BymlDirectIndexArrayChangelogBuilder : IBymlArrayChangelogBuilder
{
    public static readonly BymlDirectIndexArrayChangelogBuilder Instance = new();
    
    public bool LogChanges(ref BymlTrackingInfo info, ref Byml root, BymlArray src, BymlArray vanilla)
    {
        (var isVanillaSmaller, var larger, var smaller) = (vanilla.Count < src.Count)
            ? (true, src, vanilla)
            : (false, vanilla, src);

        BymlArrayChangelog changelog = [];

        var i = 0;

        for (; i < smaller.Count; i++) {
            var srcEntry = src[i];
            if (!BymlChangelogBuilder.LogChangesInline(ref info, ref srcEntry, vanilla[i])) {
                changelog.Add((i, BymlChangeType.Edit, srcEntry));
            }
        }

        for (; i < larger.Count; i++) {
            if (isVanillaSmaller) {
                changelog.Add((i, BymlChangeType.Add, src[i]));
                continue;
            }
            
            changelog.Add((i, BymlChangeType.Remove, new Byml()));
        }

        root = changelog;
        return changelog.Count == 0;
    }
}