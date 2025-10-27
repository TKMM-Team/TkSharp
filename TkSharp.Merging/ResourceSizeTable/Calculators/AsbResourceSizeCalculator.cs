using System.Runtime.CompilerServices;
using Revrs.Extensions;

namespace TkSharp.Merging.ResourceSizeTable.Calculators;

public sealed class AsbResourceSizeCalculator : ITkResourceSizeCalculator
{
    public static int MinBufferSize => -1;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    public static uint GetResourceSize(in Span<byte> data)
    {
        var nodeCount = data[0x10..0x14].Read<uint>();
        var exbOffset = data[0x60..0x64].Read<int>();
        var size = 552 + 40 * nodeCount;

        if (exbOffset != 0) {
            var exbCountOffset = data[(exbOffset + 0x20)..].Read<int>();
            var exbSignatureCount = data[(exbOffset + exbCountOffset)..].Read<uint>();
            size += 16 + (exbSignatureCount + 1) / 2 * 8;
        }

        return size;
    }
}