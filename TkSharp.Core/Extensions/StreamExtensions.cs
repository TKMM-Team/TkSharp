namespace TkSharp.Core.Extensions;

public static class StreamExtensions
{
    public static ArraySegment<byte> GetSpan(this MemoryStream ms)
    {
        if (!ms.TryGetBuffer(out var buffer)) {
            buffer = ms.ToArray();
        }

        return buffer;
    }
}