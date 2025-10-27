using LibHac.Common.Keys;
using TkSharp.Extensions.LibHac.Models;
using TkSharp.Extensions.LibHac.Util;

namespace TkSharp.Extensions.LibHac;

internal struct TkExtensibleRomConfig
{
    public TkExtensibleConfig<string> PreferredVersion = new(TkExtensibleConfigType.None);
    
    public TkExtensibleConfig<string> KeysFolder = new(TkExtensibleConfigType.Folder);
    
    public TkExtensibleConfig<IEnumerable<string>> ExtractedGameDumpFolderPath = new(TkExtensibleConfigType.Folder);
    
    public TkExtensibleConfig<string> SdCard = new(TkExtensibleConfigType.Folder, CheckSdCard);
    
    public TkExtensibleConfig<IEnumerable<string>> PackagedBaseGame = new(TkExtensibleConfigType.Path, CheckPackagedFile);
    
    public TkExtensibleConfig<IEnumerable<string>> PackagedUpdate = new(TkExtensibleConfigType.Path, CheckPackagedFile);
    
    public TkExtensibleConfig<IEnumerable<string>> NandFolders = new(TkExtensibleConfigType.Path, CheckNandFolder);

    public TkExtensibleRomConfig()
    {
    }

    private static bool CheckPackagedFile(IEnumerable<string> values, KeySet keys, SwitchFsContainer? switchFsContainer)
    {
        var result = false;
        var hasUpdate = false;

        foreach (var path in values) {
            result = TkGameRomUtils.IsValid(keys, path, out var hasUpdateInline, switchFsContainer);
            if (!hasUpdate) hasUpdate = hasUpdateInline;
        }
        
        return result || hasUpdate;
    }

    private static bool CheckSdCard(string value, KeySet keys, SwitchFsContainer? switchFsContainer)
    {
        var result = TkSdCardUtils.CheckSdCard(keys, value, out var hasUpdate, switchFsContainer);
        return result || hasUpdate;
    }

    private static bool CheckNandFolder(IEnumerable<string> values, KeySet keys, SwitchFsContainer? switchFsContainer)
    {
        var result = false;
        var hasUpdate = false;

        foreach (var path in values) {
            result = TkNandUtils.IsValid(keys, path, out var hasUpdateInline, switchFsContainer);
            if (!hasUpdate) hasUpdate = hasUpdateInline;
        }

        return result || hasUpdate;
    }
}