using CommunityToolkit.HighPerformance;
using TkSharp.Core.Extensions;
using TkSharp.Core.Models;

namespace TkSharp.Core.IO.Serialization;

public static class TkChangelogWriter
{
    internal const uint MAGIC = 0x4C434B54; 
    
    public static void Write(in Stream output, TkChangelog changelog)
    {
        output.Write(MAGIC);
        output.Write(changelog.BuilderVersion);
        output.Write(changelog.GameVersion);
        WriteChangelogFiles(output, changelog.ChangelogFiles);
        WriteFileList(output, changelog.MalsFiles);
        WritePatchFiles(output, changelog.PatchFiles);
        WriteCheatFiles(output, changelog.CheatFiles);
        WriteFileList(output, changelog.SubSdkFiles);
        WriteFileList(output, changelog.ExeFiles);
        WriteFileList(output, changelog.Reserved1);
        WriteFileList(output, changelog.Reserved2);
    }

    private static void WriteChangelogFiles(in Stream output, List<TkChangelogEntry> changelogs)
    {
        output.Write(changelogs.Count);

        foreach (var changelog in changelogs) {
            output.WriteString(changelog.Canonical);
            output.Write(changelog.Type);
            output.Write(changelog.Attributes);
            output.Write(changelog.ZsDictionaryId);
            WriteVersions(output, changelog.Versions);
            WriteFileList(output, changelog.ArchiveCanonicals);
        }
    }

    private static void WritePatchFiles(in Stream output, List<TkPatch> patches)
    {
        output.Write(patches.Count);

        foreach (var patch in patches) {
            output.WriteString(patch.NsoBinaryId);

            output.Write(patch.Entries.Count);
            foreach ((var key, var value) in patch.Entries) {
                output.Write(key);
                output.Write(value);
            }
        }
    }

    private static void WriteCheatFiles(in Stream output, List<TkCheat> cheats)
    {
        output.Write(cheats.Count);
        foreach (var cheat in cheats) {
            cheat.WriteBinary(output);
        }
    }

    private static void WriteFileList(in Stream output, List<string> files)
    {
        output.Write(files.Count);

        foreach (var file in files) {
            output.WriteString(file);
        }
    }
    
    private static void WriteVersions(Stream output, List<int> list)
    {
        output.Write((byte)list.Count);

        foreach (var fileVersion in list) {
            output.Write(fileVersion);
        }
    }
}