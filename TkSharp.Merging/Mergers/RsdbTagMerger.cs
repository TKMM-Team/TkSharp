using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using Revrs;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

namespace TkSharp.Merging.Mergers;

public sealed class RsdbTagMerger : Singleton<RsdbTagMerger>, ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(vanillaData, out var endianness, out ushort version).GetMap());

        foreach (var input in inputs) {
            MergeEntry(tagTable, input.Segment);
        }

        var merged = tagTable.Compile();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(vanillaData, out var endianness, out ushort version).GetMap());

        foreach (var input in inputs) {
            MergeEntry(tagTable, input);
        }

        var merged = tagTable.Compile();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(@base, out var endianness, out ushort version).GetMap());
        
        MergeEntry(tagTable, input);

        var merged = tagTable.Compile();
        merged.WriteBinary(output, endianness, version);
        
        return MergeResult.Default;
    }

    private static void MergeEntry(RsdbTagTable table, ArraySegment<byte> input)
    {
        var changelog = Byml.FromBinary(input)
            .GetMap();

        var tags = changelog["Tags"].GetArray();
        foreach (var tag in tags) {
            table.Tags.Add(tag.GetString());
        }
        
        var entries = changelog["Entries"].GetArray();
        for (int i = 0; i < entries.Count; i++) {
            var key = (
                entries[i].GetString(),
                entries[++i].GetString(),
                entries[++i].GetString()
            );

            if (!table.Entries.TryGetValue(key, out var entryTags)) {
                table.Entries[key] = entryTags = [];
            }

            var entry = entries[++i];

            if (entry.Value is BymlArray basic) {
                entryTags.AddRange(basic.Select(x => x.GetString()));
                continue;
            }

            foreach (var (_, type, target, _, _) in entry.GetArrayChangelog()) {
                if (target.Value is not string tag) {
                    continue;
                }
                
                if (type is BymlChangeType.Remove) {
                    entryTags.Remove(tag);
                    continue;
                }
                
                entryTags.Add(tag);
            }
        }
    }
}