using System.Collections.Frozen;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Merging.Common.BinaryYaml;
using TkSharp.Merging.Extensions;

namespace TkSharp.Merging.ChangelogBuilders.BinaryYaml;

public class BymlKeyedArrayChangelogBuilder(string key, string? secondaryKey = null) : IBymlArrayChangelogBuilder
{
    private readonly BymlKeyName _key = new(key, secondaryKey);

    public bool LogChanges(ref BymlTrackingInfo info, ref Byml root, BymlArray src, BymlArray vanilla)
    {
        BymlArrayChangelog changelog = [];
        var detectedAdditions = 0;

        var vanillaIndexMap = vanilla.CreateIndexCache(_key);
        using var vanillaRecordsFound = RentedBitArray.Create(vanilla.Count);

        for (var i = 0; i < src.Count; i++) {
            var node = src[i];
            if (!_key.TryGetKey(node, out var key)) {
                TkLog.Instance.LogWarning(
                    "Entry '{Index}' in '{Type}' was missing a {Key} field.",
                    i, info.Type.ToString(), _key);
                changelog.Add((index: i - detectedAdditions, BymlChangeType.Add, node));
                detectedAdditions++;
                continue;
            }
            
            if (!vanillaIndexMap.TryGetValue(key, out var vanillaIndex)) {
                var relativeIndex = i - detectedAdditions;
                changelog.Add(((vanilla.Count > relativeIndex) switch {
                    true => relativeIndex, false => i
                }, BymlChangeType.Add, node));
                
                detectedAdditions++;
                continue;
            }

            if (BymlChangelogBuilder.LogChangesInline(ref info, ref node, vanilla[vanillaIndex])) {
                src[i] = BymlChangeType.Remove;
                goto UpdateVanilla;
            }

            changelog.Add((vanillaIndex, BymlChangeType.Edit, node, key.Primary, key.Secondary));

        UpdateVanilla:
            vanillaRecordsFound[vanillaIndex] = true;
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
}