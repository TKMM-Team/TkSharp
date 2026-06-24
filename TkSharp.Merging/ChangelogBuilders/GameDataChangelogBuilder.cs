using System.IO.Hashing;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using CommunityToolkit.HighPerformance;
using TkSharp.Core;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.ChangelogBuilders.GameData;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class GameDataChangelogBuilder : Singleton<GameDataChangelogBuilder>, ITkChangelogBuilder
{
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer,
        ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite, int gameVersion)
    {
        BymlMap changelog = [];
        var src = Byml.FromBinary(srcBuffer).GetMap()["Data"].GetMap();

        // Load correct GDL version from runtime cache
        bool hasMatchingGameDataFile = path.FileVersion == GameDataCache.GetNearestGameDataVersion(gameVersion);
        var vanilla = Byml.FromBinary(
            hasMatchingGameDataFile ? vanillaBuffer : GameDataCache.GetCachedFor(path.FileVersion),
            out var endianness, out var version).GetMap()["Data"].GetMap();

        BymlTrackingInfo bymlTrackingInfo = new(path.Canonical, 0);

        foreach (var (tableName, srcEntry) in src) {
            var entries = srcEntry.GetArray();
            var vanillaEntries = vanilla[tableName].GetArray();

            if (tableName is "Bool64bitKey") {
                if (LogUInt64Entries(ref bymlTrackingInfo, gameVersion, entries, vanillaEntries) is { Count: > 0 } u64LogResult) {
                    changelog[tableName] = u64LogResult;
                }

                continue;
            }

            IBymlArrayChangelogBuilderProvider arrayChangelogBuilderProvider = tableName switch {
                "Struct" => GameDataStructArrayChangelogBuilderProvider.Instance,
                _ => GameDataArrayChangelogBuilderProvider.Instance
            };

            var tableNameHash = XxHash3.HashToUInt64(
                tableName.AsSpan().Cast<char, byte>());
            if (LogEntries(ref bymlTrackingInfo, gameVersion, tableNameHash, entries, vanillaEntries, arrayChangelogBuilderProvider) is { Count: > 0 } logResult) {
                changelog[tableName] = logResult;
            }
        }

        if (changelog.Count == 0) {
            return false;
        }

        using MemoryStream ms = new();
        ((Byml)changelog).WriteBinary(ms, endianness, version);
        ms.Seek(0, SeekOrigin.Begin);

        using var output = openWrite(path, canonical);
        ms.CopyTo(output);
        return true;
    }

    private static BymlHashMap32 LogEntries(ref BymlTrackingInfo bymlTrackingInfo, int gameVersion,
        ulong tableNameHash, BymlArray src, BymlArray vanilla, IBymlArrayChangelogBuilderProvider changelogBuilderProvider)
    {
        BymlHashMap32 changelog = [];
        HashSet<uint> expectedInVanilla = [];

        for (var i = 0; i < src.Count; i++) {
            var srcEntry = src[i];
            var entry = srcEntry.GetMap();

            if (!entry.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not uint hash) {
                // TODO: Warn | Invalid GDL entry: Missing Hash
                continue;
            }

            if (!GameDataIndex.TryGetIndex(gameVersion, tableNameHash, hash, out var index)) {
                src.RemoveAt(i--);
                goto UpdateChangelog;
            }

            expectedInVanilla.Add(hash);

            if (BymlChangelogBuilder.LogChangesInline(ref bymlTrackingInfo, ref srcEntry, vanilla[index], changelogBuilderProvider)) {
                // Skip 1:1 vanilla entry
                continue;
            }

        UpdateChangelog:
            changelog[hash] = entry;
        }

        if (src.Count == vanilla.Count) {
            return changelog;
        }

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var node in vanilla) {
            uint hash = (uint)node.GetMap()["Hash"].Value!;
            if (!expectedInVanilla.Remove(hash)) {
                changelog[hash] = BymlChangeType.Remove;
            }
        }

        return changelog;
    }

    private static BymlHashMap64 LogUInt64Entries(ref BymlTrackingInfo bymlTrackingInfo, int gameVersion, BymlArray src, BymlArray vanilla)
    {
        BymlHashMap64 changelog = [];
        HashSet<ulong> expectedInVanilla = [];

        for (var i = 0; i < src.Count; i++) {
            var srcEntry = src[i];
            var entry = srcEntry.GetMap();

            if (!entry.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not ulong hash) {
                // TODO: Warn | Invalid GDL entry: Missing Hash
                continue;
            }

            if (!GameDataIndex.TryGetIndex(gameVersion, hash, out var index)) {
                src.RemoveAt(i--);
                goto UpdateChangelog;
            }

            expectedInVanilla.Add(hash);

            if (BymlChangelogBuilder.LogChangesInline(ref bymlTrackingInfo, ref srcEntry, vanilla[index])) {
                // Skip 1:1 vanilla entry
                continue;
            }

        UpdateChangelog:
            changelog[hash] = entry;
        }

        if (src.Count == vanilla.Count) {
            return changelog;
        }

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var node in vanilla) {
            uint hash = (uint)node.GetMap()["Hash"].Value!;
            if (!expectedInVanilla.Remove(hash)) {
                changelog[hash] = BymlChangeType.Remove;
            }
        }

        return changelog;
    }

    public void Dispose()
    {
    }
}