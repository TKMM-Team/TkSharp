using CommunityToolkit.Mvvm.ComponentModel;
using TkSharp.Merging;

namespace TkSharp.Packaging;

public partial class TkChangelogFlags : ObservableObject
{
    [ObservableProperty]
    private bool _trackRemovedRsDbEntries = false;

    [ObservableProperty]
    private TkResourceSizeOverrideConfiguration _resourceSizeOverrides = new();
    
    public TkChangelogBuilderFlags GetBuilderFlags(
        IReadOnlyDictionary<string, uint>? resourceSizeOverrides = null,
        bool applyResourceSizeOverridesToAllVersions = false)
    {
        return new TkChangelogBuilderFlags(
            TrackRemovedRsDbEntries,
            resourceSizeOverrides,
            applyResourceSizeOverridesToAllVersions);
    }
}