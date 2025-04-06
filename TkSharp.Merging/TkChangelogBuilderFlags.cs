namespace TkSharp.Merging;

public readonly struct TkChangelogBuilderFlags(bool trackRemovedRsDbEntries = false)
{
    public readonly bool TrackRemovedRsDbEntries = trackRemovedRsDbEntries;
}