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
        uint signatureCount = 0;
        var exbOffset = data[0x44..].Read<uint>();

        if (exbOffset != 0) {
            var exbCountOffset = data[(int)(exbOffset + 0x20)..].Read<uint>();
            signatureCount = data[(int)(exbOffset + exbCountOffset)..].Read<uint>();
        }

        size += 16 + (signatureCount + 1) / 2 * 8;
        return size;
    }
}