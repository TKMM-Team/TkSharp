namespace TkSharp.Packaging;

public sealed class TkResourceSizeOverrideCandidate(string canonical, uint size)
{
    public string Canonical { get; } = canonical;

    public uint Size { get; } = size;
}