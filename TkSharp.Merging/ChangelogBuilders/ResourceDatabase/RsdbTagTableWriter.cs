using System.Collections.Frozen;

namespace TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

public sealed unsafe class RsdbTagTableWriter
{
    private readonly byte[] _result;
    private readonly IEnumerable<List<string?>> _entries;
    private readonly FrozenDictionary<string, int> _tagLookup;

    public RsdbTagTableWriter(IList<string> tags, IEnumerable<List<string?>> entries, int entryCount)
    {
        var size = (int)double.Ceiling(tags.Count * entryCount / 8.0);
        _result = new byte[size];
        _entries = entries;

        Dictionary<string, int> tagLookup = new(tags.Count);
        for (var i = 0; i < tags.Count; i++) {
            tagLookup[tags[i]] = i;
        }

        _tagLookup = tagLookup.ToFrozenDictionary();
    }

    public byte[] Compile()
    {
        fixed (byte* ptr = &_result[0]) {
            var bitOffset = 0;
            var current = &ptr;

            foreach (var tags in _entries) {
                FillEntry(tags, current, ref bitOffset);
            }

            return _result;
        }
    }

    public void FillEntry(IEnumerable<string?> tags, byte** current, ref int bitOffset)
    {
        var currentEntryIndex = 0;

        foreach (var tag in tags) {
            if (tag is null) {
                continue;
            }
            
            var index = _tagLookup[tag];
            MoveBy(index - currentEntryIndex, current, ref bitOffset);
            **current |= (byte)(0x1 << bitOffset);
            currentEntryIndex = index;
        }

        MoveBy(_tagLookup.Count - currentEntryIndex, current, ref bitOffset);
    }

    private static void MoveBy(int bits, byte** current, ref int bitOffset)
    {
        var byteCount = (bits += bitOffset) / 8;
        *current += byteCount;
        bitOffset = bits - (byteCount * 8);
    }
}