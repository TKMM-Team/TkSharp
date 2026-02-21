using BymlLibrary;
using BymlLibrary.Nodes.Containers;

namespace TkSharp.Merging.Mergers.GameData;

public static class SaveDataWriter
{
    private const int DEFAULT_SIZE = 0x130;

    public static readonly string[] ValidTables = [
        "Binary",
        "BinaryArray",
        "Bool",
        "Bool64bitKey",
        "BoolArray",
        "Enum",
        "EnumArray",
        "Float",
        "FloatArray",
        "Int",
        "Int64",
        "Int64Array",
        "IntArray",
        "String16",
        "String16Array",
        "String32",
        "String32Array",
        "String64",
        "String64Array",
        "UInt",
        "UInt64",
        "UInt64Array",
        "UIntArray",
        "Vector2",
        "Vector2Array",
        "Vector3",
        "Vector3Array",
        "WString16",
        "WString16Array",
        "WString32",
        "WString32Array",
        "WString64",
        "WString64Array"
    ];

    public static void CalculateMetadata(BymlMap metaData, BymlMap tables)
    {   
        var saveDataOffsets = new int[7];
        var saveDataSizes = new int[7];

        Metadata metaDataStore = new(saveDataOffsets, saveDataSizes);

        var saveDirectories = metaData["SaveDirectory"]
            .GetArray();

        foreach (var tableName in ValidTables) {
            CalculateTableMetadata(ref metaDataStore,
                tableName, tables.GetValueOrDefault(tableName)?.Value as BymlArray,
                saveDirectories);
        }

        for (var i = 0; i < 7; i++) {
            ref var saveDataOffset = ref saveDataOffsets[i];
            if (saveDataOffset > 0) saveDataOffset += DEFAULT_SIZE;

            ref var saveDataSize = ref saveDataSizes[i];
            if (saveDataSize > 0) saveDataSize += DEFAULT_SIZE;
        }

        metaData["AllDataSaveOffset"] = metaDataStore.AllDataSaveOffset;
        metaData["AllDataSaveSize"] = metaDataStore.AllDataSaveSize;
        metaData["SaveDataOffsetPos"] = new BymlArray(saveDataOffsets.Select(value => (Byml)value));
        metaData["SaveDataSize"] = new BymlArray(saveDataSizes.Select(value => (Byml)value));
    }

    private static void CalculateTableMetadata(ref Metadata metadata, string tableName, BymlArray? table, BymlArray saveDirectories)
    {
        if (table is null) {
            return;
        }
        
        var isBool64BitKey = tableName is "Bool64bitKey";
        var isArrayTable = tableName.Length > 5 && tableName.AsSpan()[^5..] is "Array";

        foreach (var entry in table.Select(x => x.GetMap())) {
            if (!entry.TryGetValue("SaveFileIndex", out var saveFileIndexEntry) || saveFileIndexEntry.Value is not int saveFileIndex) {
                continue;
            }

            if (saveFileIndex is -1 || saveDirectories[saveFileIndex].Value is "") {
                if (isBool64BitKey) {
                    continue;
                }
                
                metadata.AllDataSaveOffset += 8;
                metadata.AllDataSaveSize += CalculateEntrySize(tableName, entry, isArrayTable);
                continue;
            }

            ref var offset = ref metadata.SaveDataOffsets[saveFileIndex];
            ref var size = ref metadata.SaveDataSizes[saveFileIndex];

            if (!isBool64BitKey) {
                offset += 8;
                metadata.AllDataSaveOffset += 8;
                goto CalculateEntry;
            }

            ref var hasSizeBeenUpdated = ref metadata.SaveDataSizesHasKey[saveFileIndex];
            if (!hasSizeBeenUpdated) {
                size += 8;
                metadata.AllDataSaveSize += 8;
                hasSizeBeenUpdated = true;
            }

        CalculateEntry:
            var entrySize = CalculateEntrySize(tableName, entry, isArrayTable);
            size += entrySize;
            metadata.AllDataSaveSize += entrySize;
        }
    }

    private static int CalculateEntrySize(string tableName, BymlMap entry, bool isArrayTable)
    {
        var (entryCount, entrySize, tableTypeMask) = isArrayTable switch {
            true => (GetArrayCount(tableName, entry), 0xC, ..^5),
            false => (1, 0x8, ..)
        };

        return GetEntrySize(tableName, tableName.AsSpan()[tableTypeMask],
            entrySize, entryCount, entry);
    }

    private static int GetArrayCount(string tableName, BymlMap entry)
    {
        if (entry.TryGetValue("ArraySize", out var entryArraySize)) {
            return (int)entryArraySize.GetUInt32();
        }

        if (entry.TryGetValue("Size", out var entrySize)) {
            return (int)entrySize.GetUInt32();
        }

        if (entry.TryGetValue("DefaultValue", out var defaultValue) && defaultValue.Value is BymlArray defaultValueArray) {
            return defaultValueArray.Count;
        }

        throw new InvalidDataException(
            $"The length of an array entry in '{tableName}' could not be determined.");
    }

    private static int GetEntrySize(string tableName, ReadOnlySpan<char> tableType, int size, int count, BymlMap entry)
    {
        return tableName switch {
            "BoolArray" => size + (int)Math.Ceiling((Math.Ceiling(count / 8d) is > 3 and var value ? value : 4) / 4) * 4,
            "IntArray" or "FloatArray" or "UIntArray" or "EnumArray" => size + count * 4,
            "Binary" or "BinaryArray" => entry.GetValueOrDefault("DefaultValue")?.Value switch {
                uint defaultValue => size + count * 4
                                          + count * (int)defaultValue,
                _ => size + count * 4
            },
            _ => size + count * tableType switch {
                "UInt64" or "Int64" or "Vector2" => 8,
                "Vector3" => 12,
                "String16" => 16,
                "String32" or "WString16" => 32,
                "String64" or "WString32" => 64,
                "WString64" => 128,
                _ => 0
            }
        };
    }

    private ref struct Metadata(int[] saveDataOffsets, int[] saveDataSizes)
    {
        public readonly Span<int> SaveDataOffsets = saveDataOffsets;

        public readonly Span<int> SaveDataSizes = saveDataSizes;

        public readonly Span<bool> SaveDataSizesHasKey = new bool[7];

        public int AllDataSaveOffset = DEFAULT_SIZE;

        public int AllDataSaveSize = DEFAULT_SIZE;
    }
}