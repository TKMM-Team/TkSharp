using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp.Packaging;

public partial class TkProjectManager
{
    private static readonly string _storeFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".tk-studio", "recent.json");

    public static TkProjectManager Shared { get; } = new();
    
    public static ObservableCollection<TkProject> RecentProjects { get; } = [];

    // ReSharper disable once CollectionNeverQueried.Global
    public static readonly OptionGroupType[] OptionGroupTypes = Enum.GetValues<OptionGroupType>();

    public static int MaxRecentProjects { get; set; } = 15;

    public static TkProject NewProject(string sourceFolderPath)
    {
        var project = OpenProject(sourceFolderPath);
        project.Save();
        return project;
    }

    public static TkProject OpenProject(string sourceFolderPath)
    {
        TkProject project = new(sourceFolderPath);
        LoadProjectMetadataFromFolder(project);
        InsertRecentProject(project);
        return project;
    }

    [RelayCommand]
    // ReSharper disable once UnusedMember.Global
    public static void RemoveFromRecent(TkProject project)
    {
        RecentProjects.Remove(project);
    }

    public static void Load()
    {
        if (!File.Exists(_storeFilePath)) {
            return;
        }

        RecentProjects.Clear();
        List<string> recentProjectFolders = [];

        try {
            using var fs = File.OpenRead(_storeFilePath);
            recentProjectFolders = JsonSerializer.Deserialize<List<string>>(fs)
                                   ?? [];
        }
        catch (Exception ex) {
            TkLog.Instance.LogWarning(ex, "Failed to load recent projects");
        }

        foreach (var project in recentProjectFolders.Where(Directory.Exists).Select(projectFolder => new TkProject(projectFolder))) {
            LoadProjectMetadataFromFolder(project);
            RecentProjects.Add(project);
        }
    }

    public static void Save()
    {
        if (Path.GetDirectoryName(_storeFilePath) is string folder) {
            Directory.CreateDirectory(folder);
        }

        using var fs = File.Create(_storeFilePath);
        JsonSerializer.Serialize(fs, RecentProjects.Select(proj => proj.FolderPath));
    }

    private static void LoadProjectMetadataFromFolder(TkProject project)
    {
        string projectFilePath = Path.Combine(project.FolderPath, ".tkproj");
        FileInfo projectFile = new(projectFilePath);

        if (!projectFile.Exists || projectFile.Length == 0) {
            project.Mod = new TkMod {
                Name = Path.GetFileNameWithoutExtension(projectFilePath)
            };

            LoadProjectOptionsFromFolder(project);
            return;
        }

        using var fs = projectFile.OpenRead();
        var tkProj = JsonSerializer.Deserialize<TkProject>(fs)
                     ?? project;
        
        project.Mod = tkProj.Mod;
        project.Flags = tkProj.Flags;

        LoadProjectOptionsFromFolder(project);
    }

    public static void LoadProjectOptionsFromFolder(TkProject project)
    {
        string optionsFolderPath = Path.Combine(project.FolderPath, "options");
        if (!Directory.Exists(optionsFolderPath)) {
            return;
        }

        foreach (string optionGroupFolderPath in Directory.EnumerateDirectories(optionsFolderPath)) {
            LoadOptionGroupFolder(project, optionGroupFolderPath);
        }
    }

    public static void LoadOptionGroupFolder(TkProject project, string optionGroupFolderPath)
    {
        TkModOptionGroup group;

        string metadataFilePath = Path.Combine(optionGroupFolderPath, "info.json");
        FileInfo metadataFile = new(metadataFilePath);

        if (metadataFile is { Exists: true, Length: > 0 }) {
            using var fs = File.OpenRead(metadataFilePath);
            group = JsonSerializer.Deserialize<TkModOptionGroup>(fs)
                    ?? new TkModOptionGroup();
            goto CollectOptions;
        }

        group = new TkModOptionGroup {
            Name = Path.GetFileNameWithoutExtension(optionGroupFolderPath)
        };

    CollectOptions:
        foreach (string optionFolderPath in Directory.EnumerateDirectories(optionGroupFolderPath)) {
            LoadOptionFolder(project, group, optionFolderPath);
        }

        project.RegisterItem(group, optionGroupFolderPath);
        project.Mod.OptionGroups.Add(group);
    }

    public static void LoadOptionFolder(TkProject project, TkModOptionGroup group, string optionFolderPath)
    {
        TkModOption option;

        string metadataFilePath = Path.Combine(optionFolderPath, "info.json");
        FileInfo metadataFile = new(metadataFilePath);

        if (metadataFile is { Exists: true, Length: > 0 }) {
            using var fs = File.OpenRead(metadataFilePath);
            option = JsonSerializer.Deserialize<TkModOption>(fs) ?? new TkModOption();
            goto Result;
        }

        option = new TkModOption {
            Name = Path.GetFileNameWithoutExtension(optionFolderPath)
        };

    Result:
        project.RegisterItem(option, optionFolderPath);
        group.Options.Add(option);
    }

    private static void InsertRecentProject(TkProject project)
    {
        if (RecentProjects.Count == 0) {
            goto Default;
        }

        for (int i = 0; i < RecentProjects.Count; i++) {
            if (RecentProjects[i].FolderPath != project.FolderPath) {
                continue;
            }

            RecentProjects.RemoveAt(i);
            goto Default;
        }

    Default:
        if (RecentProjects.Count > MaxRecentProjects) {
            RecentProjects.RemoveAt(RecentProjects.Count - 1);
        }

        RecentProjects.Insert(0, project);
    }
}