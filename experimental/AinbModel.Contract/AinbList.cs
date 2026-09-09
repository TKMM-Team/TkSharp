using System.Collections;

namespace AinbModel.Contract;

/// <summary>Immutable sequence with value equality for graph records.</summary>
public sealed class AinbList<T> : IReadOnlyList<T>, IEquatable<AinbList<T>>
{
    private readonly T[] _items;

    public AinbList() => _items = [];
    public AinbList(IEnumerable<T> items) => _items = items.ToArray();
    public int Count => _items.Length;
    public T this[int index] => _items[index];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(AinbList<T>? other) => other is not null && _items.SequenceEqual(other._items);
    public override bool Equals(object? obj) => obj is AinbList<T> other && Equals(other);
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (T item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public static class AinbLists
{
    public static AinbList<T> ToAinbList<T>(this IEnumerable<T> items) => new(items);
}
