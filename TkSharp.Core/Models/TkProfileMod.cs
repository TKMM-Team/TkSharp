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

    public void EnsureOptionSelection(bool applyPackagedDefaults = false)
    {
        foreach (var group in Mod.OptionGroups) {
            if (!SelectedOptions.TryGetValue(group, out var selection)) {
                SelectedOptions[group] = selection = [];
            }

            var wasEmpty = selection.Count == 0;

            if (applyPackagedDefaults && wasEmpty && group.DefaultSelectedOptions.Count > 0) {
                switch (group.Type) {
                    case OptionGroupType.Single or OptionGroupType.SingleRequired:
                        selection.Add(group.DefaultSelectedOptions[0]);
                        break;
                    default:
                        foreach (var option in group.DefaultSelectedOptions) {
                            selection.Add(option);
                        }

                        break;
                }
            }

            if (group.Type is OptionGroupType.MultiRequired or OptionGroupType.SingleRequired
                && selection.Count == 0
                && group.Options.FirstOrDefault() is { } fallback) {
                selection.Add(fallback);
            }
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        TkProfile.OnStateChanged();
    }
}