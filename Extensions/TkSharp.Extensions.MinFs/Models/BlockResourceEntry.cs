namespace TkSharp.Extensions.MinFs.Models;

public readonly struct BlockResourceEntry(ulong blockId, long offset, int size)
{
    public readonly ulong BlockId = blockId;
    public readonly long Offset = offset;
    public readonly int Size = size;
}
