using System.Text.Json.Serialization;

namespace TkSharp.Core.Models;

[method: JsonConstructor]
public class TkChangelogEntry(string canonical, ChangelogEntryType type, TkFileAttributes attributes, int zsDictionaryId,
    List<int>? versions = null, List<string>? archiveCanonicals = null)
{
    public string Canonical { get; set; } = canonical;

    public ChangelogEntryType Type { get; init; } = type;

    public TkFileAttributes Attributes { get; init; } = attributes;

    public int ZsDictionaryId { get; init; } = zsDictionaryId;

    public List<int> Versions { get; } = versions ?? [];

    public List<string> ArchiveCanonicals { get; } = archiveCanonicals ?? [];

    public void Deconstruct(out string canonical, out ChangelogEntryType type, out TkFileAttributes attributes, out int zsDictionaryId)
    {
        canonical = Canonical;
        type = Type;
        attributes = Attributes;
        zsDictionaryId = ZsDictionaryId;
    }

    public override bool Equals(object? obj)
    {
        return obj is TkChangelogEntry entry && string.Equals(entry.Canonical, Canonical);
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(Canonical);
    }
}

public enum ChangelogEntryType
{
    Changelog,
    Copy
}