using CommunityToolkit.HighPerformance;
using TkSharp.Core.Extensions;
using TkSharp.Core.Models;
using static TkSharp.Core.IO.Serialization.TkChangelogWriter;

namespace TkSharp.Core.IO.Serialization;

public static class TkChangelogReader
{
    public static TkChangelog Read(in Stream input, ITkSystemSource? source,
        TkSystemVersion systemVersion = TkSystemVersion.Latest)
    {
        if (input.Read<uint>() != MAGIC) {
            throw new InvalidDataException(
                "Invalid totk changelog magic.");
        }

        var result = new TkChangelog {
            BuilderVersion = input.Read<int>(),
            GameVersion = input.Read<int>(),
            Source = source
        };
        
        int changelogFileCount = input.Read<int>();
        for (int i = 0; i < changelogFileCount; i++) {
            TkChangelogEntry entry = new(
                input.ReadString()!,
                input.Read<ChangelogEntryType>(),
                input.Read<TkFileAttributes>(),
                input.Read<int>(),
                ReadVersions(input)
            );

            if (systemVersion > TkSystemVersion.V1) {
                ReadFileList(input, entry.ArchiveCanonicals);
            }
            
            result.ChangelogFiles.Add(
                entry
            );
        }

        ReadFileList(input, result.MalsFiles);
        
        int patchFileCount = input.Read<int>();
        for (int i = 0; i < patchFileCount; i++) {
            result.PatchFiles.Add(
                ReadTkPatch(input)
            );
        }
        
        int cheatFileCount = input.Read<int>();
        for (int i = 0; i < cheatFileCount; i++) {
            result.CheatFiles.Add(
                TkCheat.FromBinary(input)
            );
        }
        
        ReadFileList(input, result.SubSdkFiles);
        ReadFileList(input, result.ExeFiles);
        ReadFileList(input, result.Reserved1);
        ReadFileList(input, result.Reserved2);
        
        return result;
    }

    private static List<int> ReadVersions(Stream input)
    {
        int count = input.Read<byte>();
        List<int> result = new(count);
        for (int i = 0; i < count; i++) {
            result.Add(input.Read<int>());
        }

        return result;
    }

    private static TkPatch ReadTkPatch(in Stream input)
    {
        string nsoBinaryId = input.ReadString()!;
        var result = new TkPatch(nsoBinaryId);
        
        int entryCount = input.Read<int>();
        for (int i = 0; i < entryCount; i++) {
            result.Entries[input.Read<uint>()] = input.Read<uint>();
        }

        return result;
    }

    private static void ReadFileList(in Stream input, IList<string> result)
    {
        int count = input.Read<int>();
        for (int i = 0; i < count; i++) {
            result.Add(input.ReadString()!);
        }
    }
}