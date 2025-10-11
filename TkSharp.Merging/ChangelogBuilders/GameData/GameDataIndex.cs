using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

namespace TkSharp.Merging.ChangelogBuilders.GameData;

public class GameDataIndex
{
    private const uint MAGIC = 0x544C4350;
    private const uint STANDARD_TABLES_MAGIC = 0x4454535F;
    private const uint U64_BIT_TABLES_MAGIC = 0x54423436;
    private const uint TBL_MAGIC = 0x4C42545F;

    private static readonly int[] _versions;
    private static readonly FrozenDictionary<int, Table> _tables;

    static GameDataIndex()
    {
        using var stream = typeof(GameDataIndex).Assembly
            .GetManifestResourceStream("TkSharp.Merging.Resources.GameDataIndex.bpclt")!;

        if (stream.Read<int>() != MAGIC) {
            throw new InvalidDataException("Invalid GameDataIndex magic");
        }

        int versionCount = stream.Read<int>();

        _versions = new int[versionCount];
        Dictionary<int, Table> tables = new(versionCount);

        for (int vi = 0; vi < versionCount; vi++) {
            int version = _versions[vi] = stream.Read<int>();

            if (stream.Read<uint>() != STANDARD_TABLES_MAGIC) {
                throw new InvalidDataException("Standard tables header not found.");
            }

            int tableCount = stream.Read<int>();
            Dictionary<ulong, FrozenDictionary<uint, int>> standardLookups = new(tableCount);

            for (int i = 0; i < tableCount; i++) {
                var lookupTable = ReadTable<uint>(stream, out ulong lookupTableHash);
                standardLookups[lookupTableHash] = lookupTable;
            }

            if (stream.Read<uint>() != U64_BIT_TABLES_MAGIC) {
                throw new InvalidDataException("64-bit table header not found.");
            }

            tables[version] = new Table(standardLookups.ToFrozenDictionary(), ReadTable<ulong>(stream, out _));
        }

        _tables = tables.ToFrozenDictionary();
        Array.Sort(_versions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetIndex(int version, ulong tableNameHash, uint hash, out int index)
    {
        return _tables[GetBestVersion(version)].Lookup[tableNameHash].TryGetValue(hash, out index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetIndex(int version, ulong hash, out int index)
    {
        return _tables[GetBestVersion(version)].LookupUInt64.TryGetValue(hash, out index);
    }

    private static int GetBestVersion(int version)
    {
        int match = _versions[0];
        
        foreach (int ver in _versions) {
            if (version < ver) {
                return match;
            }
            
            match = ver;
        }

        return match;
    }

    private class Table(FrozenDictionary<ulong, FrozenDictionary<uint, int>> lookup, FrozenDictionary<ulong, int> lookupUInt64)
    {
        public readonly FrozenDictionary<ulong, FrozenDictionary<uint, int>> Lookup = lookup;

        public readonly FrozenDictionary<ulong, int> LookupUInt64 = lookupUInt64;
    }

    private static FrozenDictionary<T, int> ReadTable<T>(in Stream stream, out ulong lookupTableHash) where T : unmanaged
    {
        if (stream.Read<int>() != TBL_MAGIC) {
            throw new InvalidDataException("Table header not found.");
        }

        lookupTableHash = stream.Read<ulong>();
        int entryCount = stream.Read<int>();

        Dictionary<T, int> entries = new(entryCount);
        for (int e = 0; e < entryCount; e++) {
            entries[stream.Read<T>()] = stream.Read<int>();
        }

        return entries.ToFrozenDictionary();
    }
}