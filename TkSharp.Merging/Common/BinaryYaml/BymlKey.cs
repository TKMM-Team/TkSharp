using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BymlLibrary;

namespace TkSharp.Merging.Common.BinaryYaml;

[DebuggerDisplay("Key = {Primary?.Value}, {Secondary?.Value}")]
public readonly struct BymlKey(Byml? primary) : IEquatable<BymlKey>
{
    public bool IsEmpty => Primary is null;
    
    public readonly Byml? Primary = primary;
    
    public readonly Byml? Secondary;

    public BymlKey(Byml? primary, Byml? secondary) : this(primary)
    {
        Secondary = secondary;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not BymlKey key) {
            return false;
        }
        
        return Byml.ValueEqualityComparer.Default.Equals(Primary, key.Primary) && Byml.ValueEqualityComparer.Default.Equals(Secondary, key.Secondary);
    }

    public bool Equals(BymlKey other)
    {
        return Byml.ValueEqualityComparer.Default.Equals(Primary, other.Primary) && Byml.ValueEqualityComparer.Default.Equals(Secondary, other.Secondary);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Primary is not null
                ? Byml.ValueEqualityComparer.Default.GetHashCode(Primary) : 0,
            Secondary is not null
                ? Byml.ValueEqualityComparer.Default.GetHashCode(Secondary) : 0
        );
    }

    public override string ToString()
    {
        return $"{Primary?.Value} {(Secondary is null ? "*" : $"({Secondary?.Value ?? "Null"})")}";
    }

    public static bool operator ==(BymlKey left, BymlKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BymlKey left, BymlKey right)
    {
        return !(left == right);
    }
}