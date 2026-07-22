using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Packaging;

public partial class TkResourceSizeOverrideConfiguration : ObservableObject
{
    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private Dictionary<string, uint> _entries = [];
}