using TkSharp.Core.IO.Buffers;

namespace TkSharp.Merging.Extensions;

internal static class CollectionExtensions
{
    public static bool TryGetIndex<T>(this IList<T> list, T element, IEqualityComparer<T> comparer, RentedBitArray foundVanilla, out int index)
    {
        int len = list.Count;
        for (int i = 0; i < len; i++) {
            if (!comparer.Equals(list[i], element)) {
                continue;
            }

            // If this index has already been found,
            // look for the next match
            if (foundVanilla[i]) {
                continue;
            }

            index = i;
            return true;
        }

        index = -1;
        return false;
    }
}