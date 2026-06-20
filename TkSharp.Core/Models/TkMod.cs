using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TkSharp.Core.Models;

public sealed partial class TkMod : TkStoredItem
{
    /// <summary>
    /// The version of this mod.
    /// </summary>
    [ObservableProperty]
    private string _version = "1.0.0";

    /// <summary>
    /// The author of this mod.
    /// </summary>
    [ObservableProperty]
    private string _author = string.Empty;

    /// <summary>
    /// The contributors of this mod.
    /// </summary>
    public ObservableCollection<TkModContributor> Contributors { get; init; } = [];

    /// <summary>
    /// The option groups in this mod.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<TkModOptionGroup> OptionGroups { get; init; } = [];

    /// <summary>
    /// The dependencies of this mod.
    /// </summary>
    public ObservableCollection<TkModDependency> Dependencies { get; init; } = [];

    public bool HasXLink => Changelog.ChangelogFiles.Concat(
            OptionGroups.SelectMany(grp => grp.Options)
                .SelectMany(option => option.Changelog.ChangelogFiles))
        .Any(x => x.Canonical.EndsWith(".belnk") || x.Canonical.EndsWith(".bslnk"));
    
    public bool HasELink => Changelog.ChangelogFiles.Concat(
            OptionGroups.SelectMany(grp => grp.Options)
                .SelectMany(option => option.Changelog.ChangelogFiles))
        .Any(x => x.Canonical.EndsWith(".belnk"));

    public bool HasSLink => Changelog.ChangelogFiles.Concat(
            OptionGroups.SelectMany(grp => grp.Options)
                .SelectMany(option => option.Changelog.ChangelogFiles))
        .Any(x => x.Canonical.EndsWith(".bslnk"));

    [RelayCommand]
    [property: JsonIgnore]
    private void NewContributor()
    {
        Contributors.Add(new TkModContributor(string.Empty, string.Empty));
    }

    [RelayCommand]
    [property: JsonIgnore]
    private void RemoveContributor(TkModContributor target)
    {
        Contributors.Remove(target);
    }

    public TkProfileMod GetProfileMod()
    {
        return new TkProfileMod(this);
    }
}