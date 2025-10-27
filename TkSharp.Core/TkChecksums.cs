using System.Collections.Frozen;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace TkSharp.Core;

public sealed class TkChecksums
{
    private const uint MAGIC = 0x544C4350;
    
    private readonly int _version;
    private readonly FrozenDictionary<ulong, Entry[]> _entries;

    public static TkChecksums FromStream(in Stream stream)
    {
        if (stream.Read<int>() != MAGIC) {
            throw new InvalidDataException("Invalid magic");
        }
        
        var version = stream.Read<int>();
        var entryCount = stream.Read<int>();

        Dictionary<ulong, Entry[]> entries = [];
        for (var i = 0; i < entryCount; i++) {
            var key = stream.Read<ulong>();
            var count = stream.Read<int>();
            var versions = new Entry[count];
            for (var j = 0; j < count; j++) {
                versions[j] = stream.Read<Entry>();
            }

            entries[key] = versions;
        }

        return new TkChecksums(version, entries);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong GetNameHash(ReadOnlySpan<char> canonicalFileName)
    {
        var canonicalFileNameBytes = MemoryMarshal.Cast<char, byte>(canonicalFileName);
        return XxHash3.HashToUInt64(canonicalFileNameBytes);
    }

    private TkChecksums(int version, IDictionary<ulong, Entry[]> entries)
    {
        _version = version;
        _entries = entries.ToFrozenDictionary();
    }

    public bool IsFileVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion)
    {
        return IsFileVanilla(canonical, src, fileVersion, out _);
    }

    public bool IsFileVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion, out bool isEntryFound)
    {
        if (!(isEntryFound = Lookup(canonical, fileVersion, out var entry))) {
            return false;
        }

        if (entry.Size != src.Length) {
            return false;
        }

        return XxHash3.HashToUInt64(src) == entry.Hash;
    }

    private bool Lookup(ReadOnlySpan<char> canonicalFileName, int version, out Entry entry)
    {
        var key = GetNameHash(canonicalFileName);

        if (_entries.TryGetValue(key, out var entries) == false) {
            entry = default;
            return false;
        }

        entry = entries[0];
        if (version == _version) {
            return true;
        }

        for (var i = 1; i < entries.Length; i++) {
            ref var next = ref entries[i];
            if (next.Version > version) {
                break;
            }

            entry = next;
        }

        return true;
    }

    internal readonly struct Entry
    {
        public readonly int Version;
        public readonly int Size;
        public readonly ulong Hash;
    }
}