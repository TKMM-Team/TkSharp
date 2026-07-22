namespace TkSharp.Merging;

public readonly struct TkChangelogBuilderFlags(
    bool trackRemovedRsDbEntries = false,
    IReadOnlyDictionary<string, uint>? resourceSizeOverrides = null,
    bool applyResourceSizeOverridesToAllVersions = false)
{
    public readonly bool TrackRemovedRsDbEntries = trackRemovedRsDbEntries;

    public readonly IReadOnlyDictionary<string, uint>? ResourceSizeOverrides = resourceSizeOverrides;

    public readonly bool ApplyResourceSizeOverridesToAllVersions = applyResourceSizeOverridesToAllVersions;
}