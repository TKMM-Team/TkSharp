using SarcLibrary;
using TkSharp.Core;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class SarcChangelogBuilder : Singleton<SarcChangelogBuilder>, ITkChangelogBuilder
{
    private static readonly byte[] _deletedFileMark = "TKSCRMVD"u8.ToArray();
    
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags,
        ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        var vanilla = Sarc.FromBinary(vanillaBuffer);

        Sarc changelog = [];
        var sarc = Sarc.FromBinary(srcBuffer);

        foreach (var (name, data) in sarc) {
            if (!vanilla.TryGetValue(name, out var vanillaData)) {
                // Custom file, use entire content
                goto MoveContent;
            }

            if (data.AsSpan().SequenceEqual(vanillaData)) {
                // Vanilla file, ignore
                continue;
            }

            var nested = new TkPath(
                name,
                fileVersion: path.FileVersion,
                TkFileAttributes.None,
                root: "romfs",
                name
            );

            if (TkChangelogBuilder.GetChangelogBuilder(nested) is not { } builder) {
                goto MoveContent;
            }

            builder.Build(name, nested, flags, data, vanillaData,
                (_, canon, _, _) => changelog.OpenWrite(canon)
            );
            builder.Dispose();

            continue;

        MoveContent:
            changelog[name] = data;
        }

        foreach (var (key, _) in vanilla) {
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

    public void Dispose()
    {
    }
}