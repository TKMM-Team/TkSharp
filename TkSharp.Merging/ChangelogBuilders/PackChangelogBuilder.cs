using SarcLibrary;
using TkSharp.Core;

namespace TkSharp.Merging.ChangelogBuilders;

public class PackChangelogBuilder : Singleton<PackChangelogBuilder>, ITkChangelogBuilder
{
    private static readonly byte[] _deletedFileMark = "TKSCRMVD"u8.ToArray();

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags,
        ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        Sarc vanilla = Sarc.FromBinary(vanillaBuffer);

        Sarc changelog = [];
        Sarc sarc = Sarc.FromBinary(srcBuffer);

        foreach ((string name, ArraySegment<byte> data) in sarc) {
            var nested = new TkPath(
                name,
                fileVersion: path.FileVersion,
                TkFileAttributes.None,
                root: "romfs",
                name
            );
            
            if (!vanilla.TryGetValue(name, out ArraySegment<byte> vanillaData)) {
                // Custom file, use entire content
                goto MoveContent;
            }

            if (data.AsSpan().SequenceEqual(vanillaData)) {
                // Vanilla file, ignore
                continue;
            }

            if (TkChangelogBuilder.GetChangelogBuilder(nested) is not ITkChangelogBuilder builder) {
                goto MoveContent;
            }

            builder.Build(name, nested, flags, data, vanillaData,
                (tkPath, canon, _) => openWrite(tkPath, canon, archiveCanonical: canonical)
            );

            continue;

        MoveContent:
            changelog[name] = data;
        }

        foreach ((string key, _) in vanilla) {
            if (!sarc.ContainsKey(key)) {
                changelog[key] = _deletedFileMark;
            }
        }

        if (changelog.Count == 0) {
            return false;
        }

        using Stream output = openWrite(path, canonical);
        changelog.Write(output, changelog.Endianness);
        return true;
    }
}