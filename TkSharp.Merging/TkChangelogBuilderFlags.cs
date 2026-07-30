namespace TkSharp.Merging;

public readonly struct TkChangelogBuilderFlags(
    bool trackRemovedRsDbEntries = false,
    IReadOnlyDictionary<string, uint>? resourceSizeOverrides = null)
{
    public readonly bool TrackRemovedRsDbEntries = trackRemovedRsDbEntries;

    public readonly IReadOnlyDictionary<string, uint>? ResourceSizeOverrides = resourceSizeOverrides;
}