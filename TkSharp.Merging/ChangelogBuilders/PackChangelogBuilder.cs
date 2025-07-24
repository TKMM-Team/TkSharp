using CommunityToolkit.HighPerformance;
using SarcLibrary;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Merging.ChangelogBuilders;

public class PackChangelogBuilder(ITkRom tk) : ITkChangelogBuilder
{
    private const ulong PLACEHOLDER_FILE_MARK = 5927946882928102228;
    private static readonly byte[] _deletedFileMark = "TKSCRMVD"u8.ToArray();
    
    private readonly ITkRom _tk = tk;
    
    public bool CanProcessWithoutVanilla => true;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags,
        ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        Sarc sarc = Sarc.FromBinary(srcBuffer);
        
        if (vanillaBuffer.Count == 0) {
            ExtractCustom(sarc, canonical, path, flags, openWrite);
            return true;
        }
        
        Sarc vanilla = Sarc.FromBinary(vanillaBuffer);
        Sarc changelog = [];

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
                (tkPath, canon, _) => openWrite(tkPath, canon, archiveCanonical: canonical));

            continue;

        MoveContent:
            changelog[name] = [];
            using Stream inlineOut = openWrite(nested, name, archiveCanonical: canonical);
            inlineOut.Write(data);
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

    private void ExtractCustom(Sarc sarc, string canonical, in TkPath path, in TkChangelogBuilderFlags flags, OpenWriteChangelog openWrite)
    {
        foreach ((string name, ArraySegment<byte> data) in sarc) {
            var nested = new TkPath(
                name,
                fileVersion: path.FileVersion,
                TkFileAttributes.None,
                root: "romfs",
                name
            );

            using RentedBuffer<byte> vanilla = _tk.GetVanilla(name, TkFileAttributes.None);
            if (vanilla.IsEmpty) {
                goto WriteRaw;
            }

            if (data.SequenceEqual(vanilla.Segment)) {
                WritePlaceholder(nested, name, canonical, openWrite);
                continue;
            }
            
            if (TkChangelogBuilder.GetChangelogBuilder(nested) is not ITkChangelogBuilder builder) {
                goto WriteRaw;
            }

            builder.Build(name, nested, flags, data, vanilla.Segment,
                (tkPath, canon, _) => openWrite(tkPath, canon, archiveCanonical: canonical));
            
            continue;
            
        WriteRaw:
            using Stream inlineOut = openWrite(nested, name, archiveCanonical: canonical);
            inlineOut.Write(data);
        }
    }

    private static void WritePlaceholder(in TkPath path, string name, string archive, OpenWriteChangelog openWrite)
    {
        using Stream placeholderOut = openWrite(path, name, archiveCanonical: archive);
        placeholderOut.Write(PLACEHOLDER_FILE_MARK);
    }
}