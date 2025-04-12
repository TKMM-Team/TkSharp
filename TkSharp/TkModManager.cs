using System.Collections.ObjectModel;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Revrs.Extensions;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.Models;
using TkSharp.IO;
using TkSharp.IO.Serialization;
using TkSharp.IO.Writers;

namespace TkSharp;

public sealed partial class TkModManager : ObservableObject, ITkSystemProvider
{
    private bool _isStateFrozen = true;
    
    public static TkModManager CreatePortable()
    {
        string portableDataFolder = Path.Combine(AppContext.BaseDirectory, ".data");
        return Create(portableDataFolder);
    }

    public static TkModManager Create(string dataFolderPath)
    {
        string portableManagerStateFile = Path.Combine(dataFolderPath, "state.db");
        if (!File.Exists(portableManagerStateFile)) {
            return new TkModManager(dataFolderPath) {
                _isStateFrozen = false
            };
        }

        using FileStream fs = File.OpenRead(portableManagerStateFile);
        return TkModManagerSerializer.Read(fs, dataFolderPath);
    }

    public string DataFolderPath { get; }

    public string ModsFolderPath { get; }

    [ObservableProperty]
    private TkMod? _selected;

    [ObservableProperty]
    private TkProfile? _currentProfile;

    public TkModManager(string dataFolderPath)
    {
        DataFolderPath = dataFolderPath;
        ModsFolderPath = Path.Combine(dataFolderPath, "contents");

        TkProfile.StateChanged += Save;
    }

    public ObservableCollection<TkMod> Mods { get; } = [];

    public ObservableCollection<TkProfile> Profiles { get; } = [];

    public TkProfile GetCurrentProfile()
    {
        EnsureProfiles();

        return CurrentProfile ?? Profiles[0];
    }

    public IEnumerable<TkChangelog> GetMergeTargets() => GetMergeTargets(GetCurrentProfile());

    public static IEnumerable<TkChangelog> GetMergeTargets(TkProfile profile)
    {
        foreach ((TkModOptionGroup group, HashSet<TkModOption> options) in profile.Mods.SelectMany(x => x.SelectedOptions)) {
            if (group.Type is not (OptionGroupType.MultiRequired or OptionGroupType.SingleRequired)) {
                continue;
            }
            
            if (options.Count > 0 || group.Options.Count == 0) {
                continue;
            }
            
            options.Add(group.Options[0]);
        }
        
        return profile
            .Mods
            .Where(x => x.IsEnabled)
            .SelectMany(x => x.SelectedOptions.Values
                .SelectMany(group => group)
                .OrderByDescending(option => option.Priority)
                .Select(option => option.Changelog)
                .Append(x.Mod.Changelog)
            )
            .Reverse();
    }

    public ITkModWriter GetSystemWriter(TkModContext modContext)
    {
        return new SystemModWriter(this, modContext.Id);
    }

    public ITkSystemSource GetSystemSource(string relativeFolderPath)
    {
        return new TkSystemSource(
            Path.Combine(ModsFolderPath, relativeFolderPath));
    }

    public void Import(TkMod target, TkProfile? profile = null)
    {
        AddOrUpdate(target);

        profile ??= GetCurrentProfile();
        profile.AddOrUpdate(target);
        
        Save();
    }

    public void Uninstall(TkMod target)
    {
        string targetModFolder = Path.Combine(ModsFolderPath, target.Id.ToString());
        if (!Directory.Exists(targetModFolder)) {
            TkLog.Instance.LogDebug("Content for the mod '{TargetName}' could not be found in the system.",
                target.Name);
            goto Remove;
        }

        try {
            Directory.Delete(targetModFolder, true);
        }
        catch (Exception ex) {
            TkLog.Instance.LogError(ex,
                "Failed to delete content for the mod '{TargetName}'. Consider manually deleting the folder '{TargetModFolder}' and then uninstalling this mod again.",
                target.Name, targetModFolder);
            return;
        }

    Remove:
        Lock();
        
        Mods.Remove(target);

        foreach (TkProfile profile in Profiles) {
            if (profile.Mods.FirstOrDefault(x => x.Mod == target) is TkProfileMod profileMod) {
                profile.Mods.Remove(profileMod);
            }
        }

        Unlock();
        Save();
    }

    public void Save()
    {
        if (_isStateFrozen) {
            return;
        }
        
    Retry:
        Directory.CreateDirectory(DataFolderPath);

        string currentDbFile = Path.Combine(DataFolderPath, "state.db");
        string backupDbFile = Path.Combine(DataFolderPath, "state.db.bak");

        try {
            if (File.Exists(currentDbFile)) File.Copy(currentDbFile, backupDbFile, true);
        }
        catch (Exception ex) {
            TkLog.Instance.LogError(ex, "Failed to create backup. The application data cannot not be saved.");
            return;
        }

        try {
            using MemoryStream ms = new();
            TkModManagerSerializer.Write(ms, this);

            Span<byte> buffer = ms.ToArray();

            if (buffer.Read<uint>() != TkModManagerSerializer.MAGIC) {
                TkLog.Instance.LogError("Mod manager state became corrupted in memory and was not saved. Retrying...");
                goto Retry;
            }

            using FileStream fs = File.Create(currentDbFile);
            fs.Write(buffer);
        }
        catch (IOException ex) {
            TkLog.Instance.LogWarning(ex, "State save failed with an IO exception (likely concurrent saving).");
        }
        catch (Exception ex) {
            TkLog.Instance.LogError(ex, "Failed to save mod manager state.");
        }
    }

    internal void Lock()
    {
        _isStateFrozen = true;
    }

    internal void Unlock()
    {
        _isStateFrozen = false;
    }

    /// <summary>
    /// Ensure that the mod being imported is the only instance of it.
    /// </summary>
    /// <param name="target"></param>
    private void AddOrUpdate(TkMod target)
    {
        for (int i = 0; i < Mods.Count; i++) {
            if (Mods[i].Id == target.Id) {
                Mods[i] = target;
                goto UpdateProfiles;
            }
        }
        
        Mods.Add(target);
        return;
        
    UpdateProfiles:
        foreach (TkProfile profile in Profiles) {
            profile.Update(target);
        }
    }

    private void EnsureProfiles()
    {
        if (Profiles.Count > 0) {
            return;
        }

        Profiles.Add(new TkProfile {
            Name = "Default"
        });
    }

    partial void OnCurrentProfileChanged(TkProfile? value)
    {
        value?.RebaseOptions(value.Selected);

        Save();
    }
}