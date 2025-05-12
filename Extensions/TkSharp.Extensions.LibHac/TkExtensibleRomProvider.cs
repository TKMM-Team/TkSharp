using LibHac.Common.Keys;
using LibHac.Fs.Fsa;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Common;
using TkSharp.Core.Exceptions;
using TkSharp.Core.IO;
using TkSharp.Extensions.LibHac.IO;
using TkSharp.Extensions.LibHac.Models;
using TkSharp.Extensions.LibHac.Util;

namespace TkSharp.Extensions.LibHac;

public class TkExtensibleRomProvider : ITkRomProvider
{
    private readonly TkExtensibleRomConfig _config;
    private readonly TkChecksums _checksums;

    internal TkExtensibleRomProvider(TkExtensibleRomConfig config, TkChecksums checksums)
    {
        _config = config;
        _checksums = checksums;
    }

    public ITkRom GetRom()
    {
        if (TryGetRom(out _, out _, out string? error) is { } rom) {
            return rom;
        }

        throw new GameRomException(error ?? "Failed to build ROM access interface (TkRom).");
    }

    public ITkRom? TryGetRom(out bool hasBaseGame, out bool hasUpdate, out string? error)
    {
        hasBaseGame = hasUpdate = true;
        error = null;
        Func<ITkRom?> defaultValue = () => null;

        _ = _config.PreferredVersion.Get(out string? preferredVersion);
        
        int? preferredVersionValue = int.TryParse(preferredVersion?.Replace(".", string.Empty), out int parsedVersionInline)
            ? parsedVersionInline
            : null;

        TkLog.Instance.LogDebug("[ROM *] Checking Extracted Game Dump");
        if (_config.ExtractedGameDumpFolderPath.Get(out IEnumerable<string>? extractedGameDumpPaths)) {
            if (GetPreferred(extractedGameDumpPaths, preferredVersionValue, out int version) is not string extractedGameDumpPath) {
                error = TkLocalizationInterface.Locale["TkExtensibleRomProvider_InvalidGameDump"];
                goto Continue;
            }

            if (version < 110) {
                error = TkLocalizationInterface.Locale["TkExtensibleRomProvider_InvalidGameDumpVersion"];
                return null;
            }
            
            defaultValue = () => new ExtractedTkRom(extractedGameDumpPath, _checksums);

            if (version == preferredVersionValue) {
                return defaultValue();
            }
        }

    Continue:
        TkLog.Instance.LogDebug("[ROM *] Looking for Keys");
        if (TryGetKeys() is not KeySet keys) {
            hasBaseGame = hasUpdate = false;
            error = TkLocalizationInterface.Locale["TkExtensibleRomProvider_MissingKeys"];
            return defaultValue();
        }

        // Track a list of SwitchFs instances in use
        // and dispose with the ITkRom 
        SwitchFsContainer collected = [];
        Title? main = null, update = null, alternateUpdate = null;

        TkLog.Instance.LogDebug("[ROM *] Checking Packaged Base Game");
        if (TryBuild(_config.PackagedBaseGame, keys, collected, preferredVersionValue, ref main, ref update, ref alternateUpdate) is { } buildAfterBaseGame) {
            return buildAfterBaseGame;
        }

        TkLog.Instance.LogDebug("[ROM *] Checking Packaged Update");
        if (TryBuild(_config.PackagedUpdate, keys, collected, preferredVersionValue, ref main, ref update, ref alternateUpdate) is { } buildAfterUpdate) {
            return buildAfterUpdate;
        }

        TkLog.Instance.LogDebug("[ROM *] Checking SD Card");
        if (TryBuild(_config.SdCard, keys, collected, preferredVersionValue, ref main, ref update, ref alternateUpdate) is { } buildAfterSdCard) {
            return buildAfterSdCard;
        }

        TkLog.Instance.LogDebug("[ROM *] Checking NAND");
        if (TryBuild(_config.NandFolders, keys, collected, preferredVersionValue, ref main, ref update, ref alternateUpdate) is { } buildAfterNand) {
            return buildAfterNand;
        }

        if (main is not null && (update is not null || alternateUpdate is not null)) {
            if (update is null) {
                TkLog.Instance.LogWarning(
                    "[ROM *] The configured preferred version ({Version}) could not be found",
                    preferredVersionValue);
            }

            try {
                TkLog.Instance.LogDebug("[ROM *] Configuration Valid (Mixed)");
                IFileSystem fs = main.MainNca.Nca
                    .OpenFileSystemWithPatch((update ?? alternateUpdate)!.MainNca.Nca,
                        NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
                return new TkSwitchRom(fs, collected.AsFsList(), _checksums);
            }
            catch (Exception ex) {
                TkLog.Instance.LogError(ex, "[ROM *] Configuration Error");
                return defaultValue();
            }
        }

        hasBaseGame = main is not null;
        hasUpdate = update is not null;

        error = TkLocalizationInterface.Locale["TkExtensibleRomProvider_InvalidConfig", hasBaseGame, hasUpdate];
        return defaultValue();
    }

    private KeySet? TryGetKeys()
    {
        if (_config.KeysFolder.Get(out string? keysFolder)) {
            TkLog.Instance.LogDebug("[ROM *] Looking for Keys in {KeysFolder}", keysFolder);
            if (TkKeyUtils.GetKeysFromFolder(keysFolder) is KeySet keyFromFolder) {
                return keyFromFolder;
            }
        }

        if (_config.SdCard.Get(out string? sdCardFolder)) {
            TkLog.Instance.LogDebug("[ROM *] Looking for Keys in SD card '{SdCard}'", sdCardFolder);
            if (TkKeyUtils.TryGetKeys(sdCardFolder, out KeySet? keyFromSdCard)) {
                return keyFromSdCard;
            }
        }

        TkLog.Instance.LogDebug("[ROM *] Looking for roaming keys");
        TkKeyUtils.TryGetKeys(out KeySet? keys);
        return keys;
    }

    private TkSwitchRom? TryBuild<T>(in TkExtensibleConfig<T> config, KeySet keys, SwitchFsContainer collected,
        string? preferredVersion, ref Title? main, ref Title? update, ref Title? alternateUpdate)
    {
        try {
            if (!config.Get(out _, keys, collected)) {
                return null;
            }
        }
        catch (Exception ex) {
            TkLog.Instance.LogError(ex, "[ROM *] Error while building rom.");
            return null;
        }

        foreach ((string label, SwitchFs switchFs) in collected) {
            if (!switchFs.Applications.TryGetValue(TkGameRomUtils.EX_KING_APP_ID, out Application? totk)) {
                TkLog.Instance.LogDebug("[ROM *] TotK missing in {Label}", label);
                continue;
            }

            if (totk.Main is not null) {
                TkLog.Instance.LogDebug("[ROM *] Base Game found in {Label}", label);
                main = totk.Main;
            }

            if (totk.Patch is not null) {
                if (preferredVersion is not null && preferredVersion != totk.DisplayVersion) {
                    TkLog.Instance.LogDebug("[ROM *] Update {Version} found in {Label} but is not preferred.",
                        totk.DisplayVersion, label);
                    alternateUpdate = totk.Patch;
                    continue;
                }

                TkLog.Instance.LogDebug("[ROM *] Update {Version} found in {Label}", totk.DisplayVersion, label);
                update = totk.Patch;
            }

            if (main is not null && update is not null) {
                goto IsValid;
            }
        }

        return null;

    IsValid:
        try {
            TkLog.Instance.LogDebug("[ROM *] Configuration Valid");
            IFileSystem fs = main.MainNca.Nca
                .OpenFileSystemWithPatch(update.MainNca.Nca, NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
            return new TkSwitchRom(fs, collected.AsFsList(), _checksums);
        }
        catch (Exception ex) {
            TkLog.Instance.LogError(ex, "[ROM *] Configuration Error");
            return null;
        }
    }

    private static string? GetPreferred(IEnumerable<string> extractedGameDumpPaths, int? preferredVersion, out int foundVersion)
    {
        foundVersion = -1;

        if (preferredVersion is not int version) {
            foreach (string path in extractedGameDumpPaths) {
                if (TkGameDumpUtils.CheckGameDump(path, out _, out foundVersion)) {
                    return path;
                }
            }

            return null;
        }

        string? result = null;
        foreach (string gameDumpPath in extractedGameDumpPaths) {
            if (!TkGameDumpUtils.CheckGameDump(gameDumpPath, out _, out foundVersion)) {
                continue;
            }

            result = gameDumpPath;

            if (foundVersion == version) {
                return gameDumpPath;
            }
        }

        return result;
    }
}