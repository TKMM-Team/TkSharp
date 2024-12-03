using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

namespace TkSharp.Merging.Mergers;

public sealed class RsdbTagMerger(TkZstd zs) : ITkMerger
{
    private readonly TkZstd _zs = zs;

    public void Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version).GetMap());

        foreach (RentedBuffers<byte>.Entry input in inputs) {
            MergeEntry(tagTable, input.Segment);
        }

        Byml merged = tagTable.Compile();
        WriteOutput(entry, merged, endianness, version, output);
    }

    public void Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(vanillaData, out Endianness endianness, out ushort version).GetMap());

        foreach (ArraySegment<byte> input in inputs) {
            MergeEntry(tagTable, input);
        }

        Byml merged = tagTable.Compile();
        WriteOutput(entry, merged, endianness, version, output);
    }

    public void MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        RsdbTagTable tagTable = new(
            Byml.FromBinary(@base, out Endianness endianness, out ushort version).GetMap());
        
        MergeEntry(tagTable, input);

        Byml merged = tagTable.Compile();
        WriteOutput(entry, merged, endianness, version, output);
    }

    private static void MergeEntry(RsdbTagTable table, ArraySegment<byte> input)
    {
        BymlMap changelog = Byml.FromBinary(input)
            .GetMap();

        BymlArray tags = changelog["Tags"].GetArray();
        foreach (Byml tag in tags) {
            table.Tags.Add(tag.GetString());
        }
        
        BymlArray entries = changelog["Entries"].GetArray();
        for (int i = 0; i < entries.Count; i++) {
            (string, string, string) key = (
                entries[i].GetString(),
                entries[++i].GetString(),
                entries[++i].GetString()
            );

            if (!table.Entries.TryGetValue(key, out List<string?>? entryTags)) {
                table.Entries[key] = entryTags = [];
            }

            foreach ((int index, (BymlChangeType type, Byml target)) in entries[++i].GetArrayChangelog()) {
                if (type is BymlChangeType.Remove) {
                    entryTags[index] = null;
                }
                
                if (target.Value is not string tag) {
                    continue;
                }
                
                entryTags.Add(tag);
            }
        }
    }
    
    private void WriteOutput(TkChangelogEntry entry, Byml merged, Endianness endianness, ushort version, Stream output)
    {
        using MemoryStream ms = new();
        merged.WriteBinary(ms, endianness, version);

        if (!ms.TryGetBuffer(out ArraySegment<byte> buffer)) {
            buffer = ms.ToArray();
        }

        if (!entry.Attributes.HasFlag(TkFileAttributes.HasZsExtension)) {
            output.Write(buffer);
            return;
        }

        using SpanOwner<byte> compressed = SpanOwner<byte>.Allocate(buffer.Count);
        int compressedSize = _zs.Compress(buffer, compressed.Span, entry.ZsDictionaryId);
        output.Write(compressed.Span[..compressedSize]);
    }
}