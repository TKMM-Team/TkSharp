using System.Runtime.CompilerServices;
using Revrs.Extensions;

namespace TkSharp.Merging.ResourceSizeTable.Calculators;

public sealed class BstarResourceSizeCalculator : ITkResourceSizeCalculator
{
    public static int MinBufferSize => 0xC;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    public static uint GetResourceSize(in Span<byte> data)
    {
        var entryCount = data[0x8..0xC].Read<uint>();
        return 0x120 + entryCount * 8;
    }
}