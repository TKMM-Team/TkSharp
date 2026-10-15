namespace TkSharp.Merging;

public struct TkChangelogBuilderFlags(
    bool trackRemovedRsDbEntries = false,
    IReadOnlyDictionary<string, uint>? resourceSizeOverrides = null)
{
    public static TkChangelogBuilderFlags CustomFiles { get; set; } = new() {
        IsCustomFile = true
    };
    
    public readonly bool TrackRemovedRsDbEntries = trackRemovedRsDbEntries;

    public readonly IReadOnlyDictionary<string, uint>? ResourceSizeOverrides = resourceSizeOverrides;

    public bool IsCustomFile = false;
}