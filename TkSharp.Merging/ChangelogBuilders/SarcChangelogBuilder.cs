using TkSharp.Core;
using TkSharp.Core.IO.Buffers;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class SarcChangelogBuilder : Singleton<SarcChangelogBuilder>, ITkChangelogBuilder
{
    public void Build(in TkPath path, RentedBuffer<byte> src, RentedBuffer<byte> vanilla, OpenWriteChangelog openWrite)
    {
        throw new NotImplementedException();
    }
}