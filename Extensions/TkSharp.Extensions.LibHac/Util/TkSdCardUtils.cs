using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs.Fsa;
using LibHac.Tools.Fs;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Extensions.LibHac.IO;
using TkSharp.Extensions.LibHac.Models;

namespace TkSharp.Extensions.LibHac.Util;

internal static class TkSdCardUtils
{
    public static bool CheckSdCard(KeySet keys, string sdCardFolderPath, out bool hasUpdate) => CheckSdCard(keys, sdCardFolderPath, out hasUpdate, switchFsContainer: null);

    public static bool CheckSdCard(KeySet keys, string sdCardFolderPath, out bool hasUpdate, SwitchFsContainer? switchFsContainer)
    {
        TkLog.Instance.LogDebug("[ROM *] [SD Card] Checking system");
        bool result = CheckSwitchFolder(keys, sdCardFolderPath, out hasUpdate, switchFsContainer);

        string emummcConfig = Path.Combine(sdCardFolderPath, "emuMMC", "emummc.ini");
        if (!File.Exists(emummcConfig)) {
            goto CheckDumps;
        }

        string emummcPath;

        using (FileStream fs = File.OpenRead(emummcConfig)) {
            using StreamReader reader = new(fs);
            while (reader.ReadLine() is string line) {
                ReadOnlySpan<char> lineContents = line.AsSpan();
                if (lineContents.Length < 5) {
                    continue;
                }

                if (lineContents[..4] is not "path" || lineContents[4] is not '=') {
                    continue;
                }

                emummcPath = Path.Combine(sdCardFolderPath, line[5..]);
                if (!Directory.Exists(emummcPath)) {
                    break;
                }

                goto ProcessEmummc;
            }
        }

        goto CheckDumps;

    ProcessEmummc:
        TkLog.Instance.LogDebug("[ROM *] [SD Card] Checking EmuMMC.");
        bool emummcResult = CheckSwitchFolder(keys, emummcPath, out bool emummcHasUpdate, switchFsContainer) && hasUpdate;
        if (!result) result = emummcResult;
        if (!hasUpdate) hasUpdate = emummcHasUpdate;

    CheckDumps:
        TkLog.Instance.LogDebug("[ROM *] [SD Card] Checking DBI folder.");
        string dbiFolder = Path.Combine(sdCardFolderPath, "switch", "DBI");
        CheckForDumps(keys, dbiFolder, ref result, ref hasUpdate, switchFsContainer);
    
        TkLog.Instance.LogDebug("[ROM *] [SD Card] Checking legacy NxDumpTool folder.");
        string legacyNxDumpToolFolder = Path.Combine(sdCardFolderPath, "switch", "nxdumptool");
        CheckForDumps(keys, legacyNxDumpToolFolder, ref result, ref hasUpdate, switchFsContainer);

        TkLog.Instance.LogDebug("[ROM *] [SD Card] Checking new NxDumpTool dump folder.");
        string nxDumpToolFolder = Path.Combine(sdCardFolderPath, "nxdt_rw_poc");
        CheckForDumps(keys, nxDumpToolFolder, ref result, ref hasUpdate, switchFsContainer);

        return result;
    }

    private static bool CheckSwitchFolder(KeySet keys, string target, out bool hasUpdate, SwitchFsContainer? switchFsContainer)
    {
        if (!Directory.Exists(Path.Combine(target, "Nintendo", "Contents"))) {
            hasUpdate = false;
            return false;
        }
        
        FatFileSystem.Create(out FatFileSystem? fatFileSystem, target)
            .ThrowIfFailure();
        UniqueRef<IAttributeFileSystem> fs = new(fatFileSystem);

        SwitchFs switchFs = SwitchFs.OpenSdCard(keys, ref fs);
        bool result = TkGameRomUtils.IsValid(switchFs, out hasUpdate);

        if (switchFsContainer is not null) {
            switchFsContainer.CleanupLater(fatFileSystem);
            switchFsContainer.Add((target, switchFs));
            return result;
        }

        fatFileSystem.Dispose();
        switchFs.Dispose();
        return result;
    }

    private static void CheckForDumps(KeySet keys, string dumpFolder, ref bool result, ref bool hasUpdate, SwitchFsContainer? switchFsContainer)
    {
        bool hasBaseGame = false;
        bool dumpHasUpdate = false;

        if (!Directory.Exists(dumpFolder)) {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(dumpFolder, "*.*", SearchOption.AllDirectories)) {
            if (Path.GetExtension(file.AsSpan()) is not (".nsp" or ".xci")) {
                continue;
            }

            try {
                hasBaseGame |= TkGameRomUtils.IsFileValid(keys, file, out dumpHasUpdate, switchFsContainer);
                hasUpdate |= dumpHasUpdate;
            }
            catch (HorizonResultException ex) {
                var truncatedEx = ex.ToString().Split(Environment.NewLine)[0];
                TkLog.Instance.LogWarning("Failed to read file {file}: {truncatedEx}", file, truncatedEx);
            }
            catch (Exception ex) {
                TkLog.Instance.LogWarning("An unexpected error occurred while reading split file: {ex}", ex);
            }
        }

        foreach (string folder in Directory.EnumerateDirectories(dumpFolder, "*", SearchOption.AllDirectories)) {
            string folderName = Path.GetFileName(folder);
            if (!folderName.Contains("NSP", StringComparison.OrdinalIgnoreCase) && 
                !folderName.Contains("XCI", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                hasBaseGame |= TkGameRomUtils.IsSplitFileValid(keys, folder, out dumpHasUpdate, switchFsContainer);
                hasUpdate |= dumpHasUpdate;
            }
            catch (HorizonResultException ex) {
                var truncatedEx = ex.ToString().Split(Environment.NewLine)[0];
                TkLog.Instance.LogWarning("Failed to read split file in {folder}: {truncatedEx}", folder, truncatedEx);
            }
            catch (Exception ex) {
                TkLog.Instance.LogWarning("An unexpected error occurred while reading split file: {ex}", ex);
            }
        }
        
        if (!result) result = hasBaseGame;
        if (!hasUpdate) hasUpdate = dumpHasUpdate;
    }
}