using System.Runtime.CompilerServices;
using Revrs.Extensions;

namespace TkSharp.Merging.ResourceSizeTable.Calculators;

public class AinbResourceSizeCalculator : ITkResourceSizeCalculator
{
    public static int MinBufferSize => -1;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    public static uint GetResourceSize(in Span<byte> data)
    {
        uint size = 392;
        var exbOffset = data[0x44..].Read<int>();

        if (exbOffset != 0) {
            var exbCountOffset = data[(exbOffset + 0x20)..].Read<int>();
            var exbSignatureCount = data[(exbOffset + exbCountOffset)..].Read<uint>();
            size += 16 + (exbSignatureCount + 1) / 2 * 8;
        }

        return size;
    }
}