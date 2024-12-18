using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Core.Models;

public sealed partial class TkProfileMod(TkMod mod) : ObservableObject
{
    [ObservableProperty]
    private TkMod _mod = mod;
    
    [ObservableProperty]
    private bool _isEnabled;
    
    [ObservableProperty]
    private bool _isEditingOptions;
}