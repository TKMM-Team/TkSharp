using CommunityToolkit.Mvvm.ComponentModel;
using TkSharp.Merging;

namespace TkSharp.Packaging;

public partial class TkChangelogFlags : ObservableObject
{
    [ObservableProperty]
    private bool _trackRemovedRsDbEntries = false;
    
    public TkChangelogBuilderFlags GetBuilderFlags()
    {
        return new TkChangelogBuilderFlags(TrackRemovedRsDbEntries);
    }
}