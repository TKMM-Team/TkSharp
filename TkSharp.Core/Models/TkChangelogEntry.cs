using System.Text.Json.Serialization;

namespace TkSharp.Core.Models;

[method: JsonConstructor]
public class TkChangelogEntry(string canonical, ChangelogEntryType type, TkFileAttributes attributes, int zsDictionaryId)
{
    public string Canonical { get; set; } = canonical;

    public ChangelogEntryType Type { get; init; } = type;

    public TkFileAttributes Attributes { get; init; } = attributes;

    public int ZsDictionaryId { get; init; } = zsDictionaryId;

    public void Deconstruct(out string canonical, out ChangelogEntryType type, out TkFileAttributes attributes, out int zsDictionaryId)
    {
        canonical = Canonical;
        type = Type;
        attributes = Attributes;
        zsDictionaryId = ZsDictionaryId;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not TkChangelogEntry entry) {
            return false;
        }

        return entry.Canonical == Canonical &&
               entry.Type == Type &&
               entry.Attributes == Attributes &&
               entry.ZsDictionaryId == ZsDictionaryId;
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(Canonical, Type, Attributes, ZsDictionaryId);
    }
}

public enum ChangelogEntryType
{
    Changelog,
    Copy
}