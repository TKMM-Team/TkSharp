using BymlLibrary;
using BymlLibrary.Nodes.Containers;

namespace TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

public class RsdbTagTable
{
    public const string PATH_LIST = "PathList";
    public const string TAG_LIST = "TagList";
    public const string BIT_TABLE = "BitTable";
    public const string RANK_TABLE = "RankTable";

    private readonly byte[] _rankTable;
    public Dictionary<(string, string, string), List<string?>> Entries { get; } = [];
    public List<string> Tags { get; }

    public RsdbTagTable(BymlMap root)
    {
        var paths = root[PATH_LIST].GetArray();
        var tags = root[TAG_LIST].GetArray();
        Span<byte> bitTable = root[BIT_TABLE].GetBinary();
        _rankTable = root[RANK_TABLE].GetBinary();

        for (var i = 0; i < paths.Count; i++) {
            var entryIndex = i / 3;
            var key = (
                paths[i].GetString(), paths[++i].GetString(), paths[++i].GetString()
            );

            Entries[key] = GetEntryTags<List<string?>>(entryIndex, tags, bitTable);
        }

        Tags = [.. tags.Select(x => x.GetString())];
    }

    public Byml Compile()
    {
        List<KeyValuePair<(string, string, string), List<string?>>> entries = [..
            Entries.OrderBy(x => x.Key, RsdbTagTableKeyComparer.Instance)
        ];

        var paths = CollectPaths(entries);
        Tags.Sort(StringComparer.Ordinal);

        // Distinct inline
        string prevTag = Tags[0];
        for (int i = 1; i < Tags.Count; i++) {
            var tag = Tags[i];
            
            if (prevTag == tag) {
                Tags.RemoveAt(i);
                i--;
            }
            
            prevTag = tag;
        }

        return new BymlMap {
            { BIT_TABLE, CompileBitTable(entries) },
            { PATH_LIST, paths },
            { RANK_TABLE, _rankTable },
            { TAG_LIST, new BymlArray(Tags.Select(x => (Byml)x)) },
        };
    }

    private byte[] CompileBitTable(List<KeyValuePair<(string, string, string), List<string?>>> entries)
    {
        RsdbTagTableWriter writer = new(Tags, entries.Select(x => x.Value), entries.Count);
        return writer.Compile();
    }

    private BymlArray CollectPaths(List<KeyValuePair<(string, string, string), List<string?>>> entries)
    {
        BymlArray paths = new(Entries.Count * 3);
        foreach (((string Prefix, string Name, string Suffix) entry, var tags) in entries) {
            paths.Add(entry.Prefix);
            paths.Add(entry.Name);
            paths.Add(entry.Suffix);
            tags.Sort(StringComparer.Ordinal);
        }

        return paths;
    }

    public static unsafe T GetEntryTags<T>(int entryIndex, BymlArray tags, Span<byte> bitTable) where T : ICollection<string?>, new()
    {
        T entryTags = [];

        var index = entryIndex * tags.Count;
        var bitOffset = index % 8;

        fixed (byte* ptr = &bitTable[index / 8]) {
            var current = ptr;

            foreach (var tagEntry in tags) {
                if ((*current >> bitOffset & 1) == 1) {
                    entryTags.Add(tagEntry.GetString());
                }

                switch (bitOffset) {
                    case 7:
                        bitOffset = 0;
                        current++;
                        continue;
                    default:
                        bitOffset++;
                        continue;
                }
            }
        }

        return entryTags;
    }
}