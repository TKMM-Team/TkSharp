using System.Collections.Frozen;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance;
using Revrs;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Common.BinaryYaml;
using TkSharp.Merging.Extensions;
using TkSharp.Merging.Mergers.BinaryYaml;

namespace TkSharp.Merging.Mergers;

public sealed class BymlMerger : Singleton<BymlMerger>, ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Byml merged = Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version);
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (RentedBuffers<byte>.Entry input in inputs) {
            Byml changelog = Byml.FromBinary(input.Span);
            Merge(merged, changelog, tracking);
        }
        
        tracking.Apply();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Byml merged = Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version);
        BymlMergeTracking tracking = new(entry.Canonical);

        foreach (ArraySegment<byte> input in inputs) {
            Byml changelog = Byml.FromBinary(input);
            Merge(merged, changelog, tracking);
        }
        
        tracking.Apply();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        Byml merged = Byml.FromBinary(@base, out Endianness endianness, out ushort version);
        Byml changelog = Byml.FromBinary(input);
        BymlMergeTracking tracking = new(entry.Canonical);

        Merge(merged, changelog, tracking);
        
        tracking.Apply();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public static void Merge(Byml @base, Byml changelog, BymlMergeTracking tracking)
    {
        switch (@base.Value) {
            case IDictionary<string, Byml> map:
                MergeMap(map, changelog.GetMap(), tracking);
                break;
            case IDictionary<uint, Byml> hashMap32:
                MergeMap(hashMap32, changelog.GetHashMap32(), tracking);
                break;
            case IDictionary<ulong, Byml> hashMap64:
                MergeMap(hashMap64, changelog.GetHashMap64(), tracking);
                break;
            case BymlArray array when changelog.Value is BymlArrayChangelog arrayChangelog:
                MergeArray(array, arrayChangelog, arrayName: null, tracking);
                break;
            case BymlArray existingCustomArray when changelog.Value is BymlArray customArray:
                existingCustomArray.AddRange(customArray);
                break;
            default:
                throw new NotSupportedException(
                    $"Merging BYML changelog type '{changelog.Type}' with vanilla/base type '{@base.Type}' is not supported.");
        }
    }

    public static void MergeMap<T>(IDictionary<T, Byml> @base, IDictionary<T, Byml> changelog, BymlMergeTracking tracking) where T : notnull
    {
        tracking.Depth++;
        
        foreach ((T key, Byml entry) in changelog) {
            if (entry.Value is BymlChangeType.Remove) {
                tracking.GetMapTrackingEntry(@base).Add(key);
                continue;
            }

            // In every case, this key is re-added, or used in some way
            tracking.RemoveMapTrackingEntry(@base, key);
            
            if (!@base.TryGetValue(key, out Byml? baseEntry)) {
                @base[key] = entry;
                continue;
            }

            if (key is string keyName && entry.Value is BymlArrayChangelog arrayChangelog && baseEntry.Value is BymlArray baseArray) {
                MergeArray(baseArray, arrayChangelog, keyName, tracking);
                continue;
            }

            if (entry.Value is IBymlNode && baseEntry.Value is IBymlNode) {
                Merge(baseEntry, entry, tracking);
                continue;
            }

            @base[key] = entry;
        }
        
        tracking.Depth--;
    }

    public static void MergeArray(BymlArray @base, BymlArrayChangelog changelog, string? arrayName, BymlMergeTracking tracking)
    {
        List<(int InsertIndex, Byml Entry)>? additions = null;

        BymlKeyName keyName = default;
        FrozenDictionary<BymlKey, int>? lookup = null;
        
        if (arrayName is not null && BymlMergerKeyNameProvider.Instance.GetKeyName(arrayName, tracking.Type, tracking.Depth) is var bymlKeyName) {
            keyName = bymlKeyName;
        }
        
        foreach ((int i, BymlChangeType change, Byml entry, Byml? keyPrimary, Byml? keySecondary) in changelog) {
            switch (change) {
                case BymlChangeType.Add: {
                    if (!tracking.Arrays.TryGetValue(@base, out BymlMergeTrackingArrayEntry? trackingEntry)) {
                        tracking.Arrays[@base] = trackingEntry = new BymlMergeTrackingArrayEntry {
                            ArrayName = arrayName,
                            Depth = tracking.Depth
                        };
                    }

                    if (additions is null) {
                        trackingEntry.Additions.Add(additions = []);
                    } 
                    
                    additions.Add((InsertIndex: i, entry));
                    break;
                }
                case BymlChangeType.Remove: {
                    if (!tracking.Arrays.TryGetValue(@base, out BymlMergeTrackingArrayEntry? trackingEntry)) {
                        tracking.Arrays[@base] = trackingEntry = new BymlMergeTrackingArrayEntry();
                    }
                    
                    trackingEntry.Removals.Add(i);
                    break;
                }
                case BymlChangeType.Edit: {
                    if (tracking.Arrays.TryGetValue(@base, out BymlMergeTrackingArrayEntry? trackingEntry)) {
                        trackingEntry.Removals.Remove(i);
                    }
                    
                    BymlKey key = new(keyPrimary, keySecondary);
                    ref Byml baseEntry = ref GetBestMatch(@base, i, key, keyName, ref lookup);
                    
                    if (entry.Value is IBymlNode) {
                        Merge(baseEntry, entry, tracking);
                        continue;
                    }

                    baseEntry = entry;
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Invalid array changelog entry type: '{change}'");
            }
        }
    }

    private static ref Byml GetBestMatch(BymlArray @base, int index, BymlKey key, BymlKeyName keyName, ref FrozenDictionary<BymlKey, int>? lookup)
    {
        if (keyName.IsEmpty || key.IsEmpty || @base.Count > index && keyName.GetKey(@base[index]) == key) {
            goto GetResult;
        }
        
        lookup ??= @base.CreateIndexCache(keyName);
        if (!lookup.TryGetValue(key, out index)) {
            throw new InvalidOperationException(
                $"Failed to locate an entry with the key '{key}'.");
        }
        
    GetResult:
        return ref @base.AsSpan()[index];
    }
}