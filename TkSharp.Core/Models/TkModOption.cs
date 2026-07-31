using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TkSharp.Core.Common;

namespace TkSharp.Core.Models;

public sealed partial class TkModOption : TkStoredItem
{
    private TkProfileOptionStateLookup? _profileStateStorage;
    private TkOptionSelectionFlagsLookup? _selectionFlagsStorage;
    private bool _suppressSelectionFlagsSync;

    [JsonIgnore]
    public TkProfileOptionStateLookup StateLookup
        => _profileStateStorage ?? throw new InvalidOperationException("Profile state storage is not initialized.");

    [ObservableProperty]
    private int _priority = -1;

    [ObservableProperty]
    private bool _isDefaultSelected;

    [JsonIgnore]
    public bool IsEnabled {
        get => StateLookup.GetIsEnabled();
        set {
            OnPropertyChanging();
            StateLookup.SetIsEnabled(value);
            OnPropertyChanged();
            TkProfile.OnStateChanged();
        }
    }

    [JsonIgnore]
    public bool CanChangeState => StateLookup.CanChangeState();

    [JsonIgnore]
    public bool CanChangeDefaultSelected
        => _selectionFlagsStorage?.CanChangeDefaultSelected() ?? true;

    public void InitializeProfileState(TkModOptionGroup group, TkProfileMod parent)
    {
        _profileStateStorage = new TkProfileOptionStateLookup(this, group, parent);
    }

    public void InitializeSelectionFlags(TkModOptionGroup group)
    {
        _selectionFlagsStorage = new TkOptionSelectionFlagsLookup(this, group);
    }

    public void UpdateState()
    {
        OnPropertyChanged(nameof(CanChangeState));
    }

    public void UpdateSelectionFlags()
    {
        OnPropertyChanged(nameof(IsDefaultSelected));
        OnPropertyChanged(nameof(CanChangeDefaultSelected));
    }

    public void SetSelectionFlagsFromCollections(bool isDefaultSelected)
    {
        _suppressSelectionFlagsSync = true;
        try {
            IsDefaultSelected = isDefaultSelected;
        }
        finally {
            _suppressSelectionFlagsSync = false;
        }
    }

    partial void OnIsDefaultSelectedChanged(bool value)
    {
        if (!_suppressSelectionFlagsSync) {
            _selectionFlagsStorage?.SetIsDefaultSelected(value);
        }
    }
}