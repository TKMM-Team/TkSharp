using System.Collections.Frozen;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Merging.Common.BinaryYaml;

namespace TkSharp.Merging.Extensions;

public static class BymlExtensions
{
    public static FrozenDictionary<BymlKey, int> CreateIndexCache(this BymlArray array, in BymlKeyName keyName)
    {
        switch (array.Count) {
            case 0:
            case 1 when array[0].Type != BymlNodeType.Map:
                return FrozenDictionary<BymlKey, int>.Empty;
        }

        Dictionary<BymlKey, int> indexCache = new();

        for (var i = 0; i < array.Count; i++) {
            var entry = array[i];
            if (!keyName.TryGetKey(entry, out var key)) {
                TkLog.Instance.LogWarning(
                    "Invalid BYML key name, vanilla entry at '{Index}' does not match the key type {KeyName}", i, keyName);
                continue;
            }
            
            indexCache[key] = i;
        }
        
        return indexCache.ToFrozenDictionary();
    }
}