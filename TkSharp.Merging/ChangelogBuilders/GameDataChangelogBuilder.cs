using System.IO.Hashing;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using BymlLibrary.Nodes.Containers.HashMap;
using CommunityToolkit.HighPerformance;
using Revrs;
using TkSharp.Core;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.ChangelogBuilders.GameData;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class GameDataChangelogBuilder : Singleton<GameDataChangelogBuilder>, ITkChangelogBuilder
{
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        BymlMap changelog = [];
        var src = Byml.FromBinary(srcBuffer).GetMap()["Data"].GetMap();
        var vanilla = Byml.FromBinary(vanillaBuffer, out var endianness, out ushort version).GetMap()["Data"].GetMap();

        BymlTrackingInfo bymlTrackingInfo = new(path.Canonical, 0);

        foreach ((string tableName, var srcEntry) in src) {
            var entries = srcEntry.GetArray();
            var vanillaEntries = vanilla[tableName].GetArray();

            if (tableName is "Bool64bitKey") {
                if (LogUInt64Entries(ref bymlTrackingInfo, path.FileVersion, entries, vanillaEntries) is { Count: > 0 } u64LogResult) {
                    changelog[tableName] = u64LogResult;
                }

                continue;
            }

            IBymlArrayChangelogBuilderProvider arrayChangelogBuilderProvider = tableName switch {
                "Struct" => GameDataStructArrayChangelogBuilderProvider.Instance,
                _ => GameDataArrayChangelogBuilderProvider.Instance
            };

            ulong tableNameHash = XxHash3.HashToUInt64(
                tableName.AsSpan().Cast<char, byte>());
            if (LogEntries(ref bymlTrackingInfo, path.FileVersion, tableNameHash, entries, vanillaEntries, arrayChangelogBuilderProvider) is { Count: > 0 } logResult) {
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

    private static BymlHashMap32 LogEntries(ref BymlTrackingInfo bymlTrackingInfo, int gameDataListFileVersion,
        ulong tableNameHash, BymlArray src, BymlArray vanilla, IBymlArrayChangelogBuilderProvider changelogBuilderProvider)
    {
        BymlHashMap32 changelog = [];

        for (int i = 0; i < src.Count; i++) {
            var srcEntry = src[i];
            var entry = srcEntry.GetMap();
            if (!entry.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not uint hash) {
                continue;
            }

            if (!GameDataIndex.TryGetIndex(gameDataListFileVersion, tableNameHash, hash, out int index)) {
                src.RemoveAt(i);
                i--;
                goto UpdateChangelog;
            }

            // Vanilla entry [hash] accounted for

            if (BymlChangelogBuilder.LogChangesInline(ref bymlTrackingInfo, ref srcEntry, vanilla[index], changelogBuilderProvider)) {
                continue;
            }

        UpdateChangelog:
            changelog[hash] = entry;
        }

        if (src.Count == vanilla.Count) {
            return changelog;
        }

        // TODO: Handle removed entries
        return changelog;
    }

    private static BymlHashMap64 LogUInt64Entries(ref BymlTrackingInfo bymlTrackingInfo, int gameDataListFileVersion, BymlArray src, BymlArray vanilla)
    {
        BymlHashMap64 changelog = [];

        foreach (var srcEntry in src) {
            var srcEntryVar = srcEntry;
            var entry = srcEntryVar.GetMap();
            if (!entry.TryGetValue("Hash", out var hashEntry) || hashEntry.Value is not ulong hash) {
                continue;
            }

            if (!GameDataIndex.TryGetIndex(gameDataListFileVersion, hash, out int index)) {
                goto UpdateChangelog;
            }

            if (BymlChangelogBuilder.LogChangesInline(ref bymlTrackingInfo, ref srcEntryVar, vanilla[index])) {
                continue;
            }

        UpdateChangelog:
            changelog[hash] = entry;
        }

        return changelog;
    }

    public void Dispose()
    {
    }
}