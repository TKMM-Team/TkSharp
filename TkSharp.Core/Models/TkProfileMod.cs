using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Core.Models;

public sealed partial class TkProfileMod(TkMod mod) : ObservableObject
{
    public Dictionary<TkModOptionGroup, HashSet<TkModOption>> SelectedOptions { get; set; } = [];
    
    [ObservableProperty]
    private TkMod _mod = mod;
    
    [ObservableProperty]
    private bool _isEnabled = true;
    
    [ObservableProperty]
    private bool _isEditingOptions;

    public override bool Equals(object? obj)
    {
        if (obj is not TkProfileMod profileMod) {
            return false;
        }

        return profileMod.Mod.Id == Mod.Id;
    }

    public override int GetHashCode()
    {
        return Mod.GetHashCode();
    }

    public void EnsureOptionSelection()
    {
        foreach (var group in Mod.OptionGroups) {
            if (group.Type is not (OptionGroupType.MultiRequired or OptionGroupType.SingleRequired)) {
                continue;
            }

            if (!SelectedOptions.TryGetValue(group, out var selection)) {
                SelectedOptions[group] = selection = [];
            }

            if (selection.Count == 0 && group.Options.FirstOrDefault() is TkModOption option) {
                selection.Add(option);
            }
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        TkProfile.OnStateChanged();
    }
}