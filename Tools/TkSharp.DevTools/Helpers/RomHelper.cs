using LibHac.Common.Keys;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.IO;
using TkSharp.Data.Embedded;
using TkSharp.Extensions.LibHac;
using Config = TkSharp.DevTools.ViewModels.SettingsPageViewModel;

namespace TkSharp.DevTools.Helpers;

public class RomHelper : ITkRomProvider
{
    private static readonly TkChecksums _checksums = TkChecksums.FromStream(TkEmbeddedDataSource.GetChecksumsBin());
    
    public static readonly RomHelper Instance = new();

    public ITkRom GetRom()
    {
        if (Config.Shared.GameDumpFolderPath is string gamePath && Directory.Exists(gamePath)) {
            return new ExtractedTkRom(gamePath, _checksums);
        }

        if (Config.Shared.KeysFolderPath is not string keysFolderPath) {
            throw new InvalidOperationException("Invalid configuration.");
        }

        var (baseSource, basePath) = GetRomSource();
        var (updateSource, updatePath) = GetUpdateSource();

        if (baseSource is null || basePath is null || updateSource is null || updatePath is null) {
            throw new InvalidOperationException("Invalid configuration.");
        }

        Console.WriteLine($"Reading base game from {baseSource} and update from {updateSource}");
        return TkRomHelper.CreateRom(
            _checksums,
            keysFolderPath,
            baseSource.Value, basePath,
            updateSource.Value, updatePath);
    }

    private static (TkRomHelper.RomSource? Source, string? Path) GetRomSource()
    {
        if (Config.Shared.BaseGameFilePath is string path && File.Exists(path))
            return (TkRomHelper.RomSource.File, path);
        if (Config.Shared.SplitFilesPath is string splitPath && Directory.Exists(splitPath))
            return (TkRomHelper.RomSource.SplitFiles, splitPath);
        if (Config.Shared.SdCardRootPath is string sdPath && Directory.Exists(sdPath))
            return (TkRomHelper.RomSource.SdCard, sdPath);
        return (null, null);
    }

    private static (TkRomHelper.RomSource? Source, string? Path) GetUpdateSource()
    {
        if (Config.Shared.GameUpdateFilePath is string path && File.Exists(path))
            return (TkRomHelper.RomSource.File, path);
        if (Config.Shared.SdCardRootPath is string sdPath && Directory.Exists(sdPath))
            return (TkRomHelper.RomSource.SdCard, sdPath);
        return (null, null);
    }
}