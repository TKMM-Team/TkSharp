#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.

using System.Collections.Frozen;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance.Buffers;
using Revrs;
using TkSharp.Core;
using TkSharp.Merging.ChangelogBuilders.ResourceDatabase;
using static TkSharp.Merging.ChangelogBuilders.ResourceDatabase.RsdbTagTable;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class RsdbTagChangelogBuilder : Singleton<RsdbTagChangelogBuilder>, ITkChangelogBuilder
{
    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        using RsdbTagIndex vanilla = new(vanillaBuffer);

        BymlMap src = Byml.FromBinary(srcBuffer, out Endianness endianness, out ushort version).GetMap();
        BymlArray paths = src[PATH_LIST].GetArray();
        BymlArray tags = src[TAG_LIST].GetArray();
        byte[] bitTable = src[BIT_TABLE].GetBinary();

        BymlArray changelog = [];
        for (int i = 0; i < paths.Count; i++) {
            int entryIndex = i / 3;
            bool isKeyVanilla = vanilla.HasEntry(paths, ref i, out int vanillaEntryIndex, out (Byml Prefix, Byml Name, Byml Suffix) entry);
            var entryTags = GetEntryTags<HashSet<string>>(entryIndex, tags, bitTable);

            int removedCount = 0;
            var removed = SpanOwner<string>.Empty;
            if (isKeyVanilla && IsEntryVanilla(entryTags, vanilla.EntryTags[vanillaEntryIndex], out removed, out removedCount)) {
                continue;
            }

            changelog.AddRange(paths[(i - 2)..(i + 1)]);
            changelog.Add(CreateEntry(entryTags, removed.Span[..removedCount]));
            removed.Dispose();
        }

        BymlArray newTags = [];
        newTags.AddRange(tags.Where(tag => !vanilla.HasTag(tag)));

        if (changelog.Count == 0 && newTags.Count == 0) {
            return false;
        }

        Byml result = new BymlMap() {
            { "Entries", changelog },
            { "Tags", newTags }
        };

        using MemoryStream ms = new();
        result.WriteBinary(ms, endianness, version);
        ms.Seek(0, SeekOrigin.Begin);

        using Stream output = openWrite(path, canonical);
        ms.CopyTo(output);
        return true;
    }

    private static Byml CreateEntry(HashSet<string> entryTags, Span<string> removed)
    {
        int index = -1;
        BymlArrayChangelog changelog = [];

        foreach (string tag in removed) {
            changelog.Add((++index, BymlChangeType.Remove, tag));
        }

        foreach (string tag in entryTags) {
            changelog.Add((++index, BymlChangeType.Add, tag));
        }

        return changelog;
    }

    private static bool IsEntryVanilla(HashSet<string> entryTags, FrozenSet<string> vanillaEntryTags, out SpanOwner<string> removed, out int removedCount)
    {
        removed = SpanOwner<string>.Allocate(vanillaEntryTags.Count);
        removedCount = 0;

        foreach (string tag in vanillaEntryTags.Where(tag => !entryTags.Remove(tag))) {
            removed.Span[removedCount] = tag;
            removedCount++;
        }

        return entryTags.Count == 0;
    }
}