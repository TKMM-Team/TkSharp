using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TkSharp.Core.Common;

namespace TkSharp.Core.Models;

public enum OptionGroupType
{
    Multi,
    MultiRequired,
    Single,
    SingleRequired
}

public sealed partial class TkModOptionGroup : TkItem
{
    [ObservableProperty]
    private OptionGroupType _type;

    [ObservableProperty]
    private string? _iconName;
    
    [ObservableProperty]
    private int _priority = -1;
    
    [ObservableProperty]
    private bool _isEditing;

    [JsonIgnore]
    public ObservableCollection<TkModOption> Options { get; } = [];

    [JsonIgnore]
    public ObservableCollection<TkModOption> DefaultSelectedOptions { get; } = [];

    public ObservableCollection<TkModDependency> Dependencies { get; } = [];

    public TkModOptionGroup()
    {
    }

    partial void OnTypeChanged(OptionGroupType value)
        => TkOptionSelectionFlagsLookup.EnsureValidDefaultSelections(this);

    public void SyncSelectionCollectionsFromOptions()
    {
        DefaultSelectedOptions.Clear();

        foreach (var option in Options) {
            if (option.IsDefaultSelected && !DefaultSelectedOptions.Contains(option)) {
                DefaultSelectedOptions.Add(option);
            }
        }

        TkOptionSelectionFlagsLookup.EnsureValidDefaultSelections(this);
    }
}