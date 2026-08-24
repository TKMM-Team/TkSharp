using TkSharp.Core;
using TkSharp.Merging.Mergers.Asb;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class AsbBaevChangelogBuilder : Singleton<AsbBaevChangelogBuilder>, ITkChangelogBuilder
{
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags,
        ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer,
        OpenWriteChangelog openWrite, int gameVersion)
    {
        if (srcBuffer.AsSpan().SequenceEqual(vanillaBuffer))
        {
            return false;
        }

        switch (Path.GetExtension(canonical))
        {
            case ".asb":
                AsbCodec.Read(srcBuffer.ToArray());
                AsbCodec.Read(vanillaBuffer.ToArray());
                break;
            case ".baev":
                BaevCodec.Read(srcBuffer.ToArray());
                BaevCodec.Read(vanillaBuffer.ToArray());
                break;
            default:
                throw new InvalidOperationException($"Unsupported animation merge target {canonical}.");
        }

        using var output = openWrite(path, canonical);
        output.Write(srcBuffer);
        return true;
    }

    public void Dispose()
    {
    }
}
