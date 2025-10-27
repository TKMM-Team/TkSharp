using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Revrs;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers.BinaryYaml;
using TkSharp.Merging.Mergers.GameData;

namespace TkSharp.Merging.Mergers;

public sealed class GameDataMerger : Singleton<GameDataMerger>, ITkMerger
{
    private static readonly BymlRowComparer _rowComparer = new("Hash");
    
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Byml.FromBinary(vanillaData, out var endianness, out var version);
        var root = merged.GetMap();
        var baseData = root["Data"].GetMap();
        BymlMergeTracking tracking = new(entry.Canonical);
        GameDataMergeTracking gameDataTracking = new(entry.Canonical);

        foreach (var input in inputs) {
            var changelog = Byml.FromBinary(input.Span)
                .GetMap();
            MergeEntry(baseData, changelog, gameDataTracking, tracking);
        }

        tracking.Apply();
        gameDataTracking.Apply();

        SaveDataWriter.CalculateMetadata(root["MetaData"].GetMap(), baseData);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        var merged = Byml.FromBinary(vanillaData, out var endianness, out var version);
        var root = merged.GetMap();
        var baseData = root["Data"].GetMap();
        BymlMergeTracking tracking = new(entry.Canonical);
        GameDataMergeTracking gameDataTracking = new(entry.Canonical);

        foreach (var input in inputs) {
            var changelog = Byml.FromBinary(input)
                .GetMap();
            MergeEntry(baseData, changelog, gameDataTracking, tracking);
        }

        tracking.Apply();
        gameDataTracking.Apply();

        SaveDataWriter.CalculateMetadata(root["MetaData"].GetMap(), baseData);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        var merged = Byml.FromBinary(@base, out var endianness, out var version);
        var root = merged.GetMap();
        var baseData = root["Data"].GetMap();
        BymlMergeTracking tracking = new(entry.Canonical);
        GameDataMergeTracking gameDataTracking = new(entry.Canonical);

        var changelog = Byml.FromBinary(input)
            .GetMap();
        MergeEntry(baseData, changelog, gameDataTracking, tracking);

        tracking.Apply();
        gameDataTracking.Apply();

        SaveDataWriter.CalculateMetadata(root["MetaData"].GetMap(), baseData);
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    private static void MergeEntry(BymlMap merged, BymlMap changelog, GameDataMergeTracking gameDataTracking, BymlMergeTracking tracking)
    {
        foreach ((var key, var entry) in changelog) {
            var @base = merged[key].GetArray();
            switch (entry.Value) {
                case IDictionary<uint, Byml> hashMap32:
                    MergeHashMap32(@base, hashMap32, gameDataTracking, tracking, key == "Struct");
                    break;
                case IDictionary<ulong, Byml> hashMap64:
                    MergeHashMap64(@base, hashMap64, gameDataTracking, tracking);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Invalid GameDataList changelog array map type: '{entry.Type}'");
            }
            
            @base.Sort(_rowComparer);
        }
    }

    private static void MergeHashMap32(BymlArray @base, IDictionary<uint, Byml> changelog, GameDataMergeTracking gameDataTracking, BymlMergeTracking tracking, bool isStructTable)
    {
        foreach (var baseEntry in @base) {
            if (baseEntry.Value is not IDictionary<string, Byml> map ||
                !map.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not uint hash) {
                // Invalid vanilla GDL entry
                continue;
            }

            if (!changelog.Remove(hash, out var changelogEntry)) {
                continue;
            }

            if (gameDataTracking.TryGetValue(hash, out var entry)) {
                entry.Changes.Add(changelogEntry);
                continue;
            }

            BymlMerger.Merge(baseEntry, changelogEntry, tracking);
        }

        foreach ((var hash, var entry) in changelog) {
            @base.Add(entry);
            gameDataTracking[hash] = new GameDataMergeTrackingEntry(entry, isStructTable);
        }
    }

    private static void MergeHashMap64(BymlArray @base, IDictionary<ulong, Byml> changelog, GameDataMergeTracking gameDataTracking, BymlMergeTracking tracking)
    {
        foreach (var baseEntry in @base) {
            if (baseEntry.Value is not IDictionary<string, Byml> map ||
                !map.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not ulong hash) {
                // Invalid vanilla GDL entry
                continue;
            }

            if (!changelog.Remove(hash, out var changelogEntry)) {
                continue;
            }
            
            if (gameDataTracking.TryGetValue(hash, out var entry)) {
                entry.Changes.Add(changelogEntry);
                continue;
            }

            BymlMerger.Merge(baseEntry, changelogEntry, tracking);
        }

        foreach ((var hash, var entry) in changelog) {
            @base.Add(entry);
            gameDataTracking[hash] = new GameDataMergeTrackingEntry(entry, isStructTable: false);
        }
    }
}