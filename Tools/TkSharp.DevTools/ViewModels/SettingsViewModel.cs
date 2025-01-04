using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibHac.Common.Keys;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.DevTools.Helpers.Ryujinx;
using Avalonia.Platform.Storage;

namespace TkSharp.DevTools.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private static readonly string _tkConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "totk", "Config.json");

    private static readonly string _configFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TkSharp.DevTools", "Config.json");
    
    public static readonly SettingsPageViewModel Shared = Load();

    [ObservableProperty]
    private string? _keysFolderPath;

    [ObservableProperty]
    private string? _baseGameFilePath;

    [ObservableProperty]
    private string? _gameUpdateFilePath;

    [ObservableProperty]
    private string? _gameDumpFolderPath;

    [ObservableProperty]
    private string? _sdCardRootPath;

    [ObservableProperty]
    private string? _splitFilesPath;

    public static SettingsPageViewModel Load()
    {
        SettingsPageViewModel? result;

        if (!File.Exists(_configFilePath)) {
            result = new SettingsPageViewModel();
            goto FetchTkConfig;
        }

        using (FileStream fs = File.OpenRead(_configFilePath)) {
            result = JsonSerializer.Deserialize<SettingsPageViewModel>(fs)
                     ?? new SettingsPageViewModel();
        }

    FetchTkConfig:
        if (!File.Exists(_tkConfigPath)) {
            return result;
        }

        using FileStream tkConfigFs = File.OpenRead(_tkConfigPath);
        if (JsonSerializer.Deserialize<TkConfig>(tkConfigFs) is not TkConfig tkConfig) {
            return result;
        }

        result.GameDumpFolderPath = tkConfig.GamePath;
        return result;
    }

    [RelayCommand]
    [property: JsonIgnore]
    private void LoadFromRyujinx()
    {
        if (RyujinxHelper.GetKeys(out string keysFolderPath) is not KeySet keys) {
            return;
        }

        KeysFolderPath = keysFolderPath;

        List<(string FilePath, string)> foundTotkFiles = RyujinxHelper.GetTotkFiles(keys)
            .OrderBy(x => x.Version)
            .ToList();

        foreach ((string filePath, string version) in foundTotkFiles) {
            TkLog.Instance.LogInformation("Found '{FilePath}' v{Version}", filePath, version);
        }

        BaseGameFilePath = foundTotkFiles[0].FilePath;
        GameUpdateFilePath = foundTotkFiles[^1].FilePath;
    }

    [RelayCommand]
    [property: JsonIgnore]
    private void Save()
    {
        if (Path.GetDirectoryName(_configFilePath) is string folder) {
            Directory.CreateDirectory(folder);
        }

        using FileStream fs = File.Create(_configFilePath);
        JsonSerializer.Serialize(fs, this);
    }

    private record TkConfig(string GamePath);
}