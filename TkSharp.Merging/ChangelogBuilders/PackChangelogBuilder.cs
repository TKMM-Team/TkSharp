using SarcLibrary;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;

namespace TkSharp.Merging.ChangelogBuilders;

public class PackChangelogBuilder(ITkRom tk, bool disposeTkRom) : ITkChangelogBuilder
{
    private static readonly byte[] _deletedFileMark = "TKSCRMVD"u8.ToArray();
    
    private readonly ITkRom _tk = tk;
    private readonly bool _disposeTkRom = disposeTkRom;

    public bool CanProcessWithoutVanilla => true;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags,
        ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        var sarc = Sarc.FromBinary(srcBuffer);
        
        if (vanillaBuffer.Count == 0) {
            ExtractCustom(sarc, canonical, path, flags, openWrite);
            return true;
        }
        
        var vanilla = Sarc.FromBinary(vanillaBuffer);
        Sarc changelog = [];

        foreach ((string name, var data) in sarc) {
            var nested = new TkPath(
                name,
                fileVersion: path.FileVersion,
                TkFileAttributes.None,
                root: "romfs",
                name
            );
            
            if (!vanilla.TryGetValue(name, out var vanillaData)) {
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
                (tkPath, canon, _, _) => openWrite(tkPath, canon, archiveCanonical: canonical));
            builder.Dispose();

            continue;

        MoveContent:
            changelog[name] = [];
            using var inlineOut = openWrite(nested, name, archiveCanonical: canonical);
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

        using var output = openWrite(path, canonical);
        changelog.Write(output, changelog.Endianness);
        return true;
    }

    private void ExtractCustom(Sarc sarc, string canonical, in TkPath path, in TkChangelogBuilderFlags flags, OpenWriteChangelog openWrite)
    {
        foreach ((string name, var data) in sarc) {
            var nested = new TkPath(
                name,
                fileVersion: path.FileVersion,
                TkFileAttributes.None,
                root: "romfs",
                name
            );

            using var vanilla = _tk.GetVanilla(name, TkFileAttributes.None);
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

            var hasChanges = builder.Build(name, nested, flags, data, vanilla.Segment,
                (tkPath, canon, _, _) => openWrite(tkPath, canon, archiveCanonical: canonical));
            builder.Dispose();
            
            if (!hasChanges) {
                WritePlaceholder(nested, name, canonical, openWrite);
            }
            
            continue;
            
        WriteRaw:
            using var inlineOut = openWrite(nested, name, archiveCanonical: canonical);
            inlineOut.Write(data);
        }
    }

    private static void WritePlaceholder(in TkPath path, string name, string archive, OpenWriteChangelog openWrite)
    {
        openWrite(path, name, archiveCanonical: archive, ChangelogEntryType.Placeholder)
            .Dispose();
    }
    
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposeTkRom) {
            _tk.Dispose();
        }
    }
}