using System.Diagnostics.Contracts;
using LibHac.Common.Keys;
using TkSharp.Core;
using TkSharp.Core.IO.Caching;
using TkSharp.Extensions.LibHac.Util;

namespace TkSharp.Extensions.LibHac;

public class TkExtensibleRomProviderBuilder
{
    private readonly TkChecksums _checksums;
    private readonly TkPackFileLookup _packFileLookup;
    private TkExtensibleRomConfig _root = new();

    private TkExtensibleRomProviderBuilder(TkChecksums checksums, TkPackFileLookup packFileLookup)
    {
        _checksums = checksums;
        _packFileLookup = packFileLookup;
    }
    
    public static TkExtensibleRomProviderBuilder Create(TkChecksums checksums, TkPackFileLookup packFileLookup)
    {
        return new TkExtensibleRomProviderBuilder(checksums, packFileLookup);
    }
    
    public TkExtensibleRomProvider Build()
    {
        return new TkExtensibleRomProvider(_root, _checksums, _packFileLookup);
    }

    public TkExtensibleRomProviderBuilder WithPreferredVersion(string? preferredVersion)
    {
        _root.PreferredVersion.Set(() => preferredVersion);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithPreferredVersion(Func<string?> preferredVersion)
    {
        _root.PreferredVersion.Set(preferredVersion);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithKeysFolder(string? keysFolderPath)
    {
        _root.KeysFolder.Set(() => keysFolderPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithKeysFolder(Func<string?> keysFolderPath)
    {
        _root.KeysFolder.Set(keysFolderPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithExtractedGameDump(IEnumerable<string>? gameDumpPath)
    {
        _root.ExtractedGameDumpFolderPath.Set(() => gameDumpPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithExtractedGameDump(Func<IEnumerable<string>?> gameDumpPath)
    {
        _root.ExtractedGameDumpFolderPath.Set(gameDumpPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithSdCard(string? sdCardFolderPath)
    {
        _root.SdCard.Set(() => sdCardFolderPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithSdCard(Func<string?> sdCardFolderPath)
    {
        _root.SdCard.Set(sdCardFolderPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithPackagedBaseGame(IEnumerable<string>? packagedBaseGamePath)
    {
        _root.PackagedBaseGame.Set(() => packagedBaseGamePath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithPackagedBaseGame(Func<IEnumerable<string>?> packagedBaseGamePath)
    {
        _root.PackagedBaseGame.Set(packagedBaseGamePath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithPackagedUpdate(IEnumerable<string>? packagedUpdatePath)
    {
        _root.PackagedUpdate.Set(() => packagedUpdatePath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithPackagedUpdate(Func<IEnumerable<string>?> packagedUpdatePath)
    {
        _root.PackagedUpdate.Set(packagedUpdatePath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithNand(IEnumerable<string>? nandFolderPath)
    {
        _root.NandFolders.Set(() => nandFolderPath);
        return this;
    }

    public TkExtensibleRomProviderBuilder WithNand(Func<IEnumerable<string>?> nandFolderPath)
    {
        _root.NandFolders.Set(nandFolderPath);
        return this;
    }

    /// <summary>
    /// Get a report on the current state of the builder.
    /// </summary>
    /// <returns></returns>
    [Pure]
    public TkExtensibleRomReport GetReport()
    {
        var report = TkExtensibleRomReportBuilder.Create();

        if (_root.ExtractedGameDumpFolderPath.Get(out IEnumerable<string>? gameDumpPaths)) {
            const string infoKey = "Game Dump Path(s)";
            foreach (string gameDumpPath in gameDumpPaths) {
                bool hasBaseGame = TkGameDumpUtils.CheckGameDump(gameDumpPath, out bool hasUpdate);
                report.SetHasBaseGame(hasBaseGame, infoKey);
                report.SetHasUpdate(hasUpdate, infoKey);
            }
        }

        if (!TkKeyUtils.TryGetKeys(out KeySet? keys)) {
            goto Result;
        }
        
        report.SetKeys(keys);

        if (_root.SdCard.Get(out string? sdCardFolderPath)) {
            const string infoKey = "Installed or Dumped on SD Card";
            bool hasBaseGameInSdCard = TkSdCardUtils.CheckSdCard(keys, sdCardFolderPath, out bool hasUpdateInSdCard);
            report.SetHasBaseGame(hasBaseGameInSdCard, infoKey);
            report.SetHasUpdate(hasUpdateInSdCard, infoKey);
        }

        if (_root.PackagedBaseGame.Get(out IEnumerable<string>? packagedBaseGamePaths)) {
            const string packagedInfoKey = "Packaged Base Game File(s)";
            const string splitFileInfoKey = "Packaged Base Game Split File(s)";

            foreach (string packagedBaseGamePath in packagedBaseGamePaths) {
                bool hasBaseGameAsFile = TkGameRomUtils.IsFileValid(keys, packagedBaseGamePath, out bool hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGameAsFile, packagedInfoKey);
                report.SetHasUpdate(hasUpdateAsFile, packagedInfoKey);
                
                bool hasBaseGameAsSplitFile = TkGameRomUtils.IsSplitFileValid(keys, packagedBaseGamePath, out bool hasUpdateAsSplitFile);
                report.SetHasBaseGame(hasBaseGameAsSplitFile, splitFileInfoKey);
                report.SetHasUpdate(hasUpdateAsSplitFile, splitFileInfoKey);
            }
        }

        if (_root.PackagedUpdate.Get(out IEnumerable<string>? packagedUpdatePaths)) {
            const string packagedInfoKey = "Packaged Update File(s)";
            const string splitFileInfoKey = "Packaged Update Split File(s)";
            
            foreach (string packagedUpdatePath in packagedUpdatePaths) {
                bool hasBaseGameAsFile = TkGameRomUtils.IsFileValid(keys, packagedUpdatePath, out bool hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGameAsFile, packagedInfoKey);
                report.SetHasUpdate(hasUpdateAsFile, packagedInfoKey);
                
                bool hasBaseGameAsSplitFile = TkGameRomUtils.IsSplitFileValid(keys, packagedUpdatePath, out bool hasUpdateAsSplitFile);
                report.SetHasBaseGame(hasBaseGameAsSplitFile, splitFileInfoKey);
                report.SetHasUpdate(hasUpdateAsSplitFile, splitFileInfoKey);
            }
        }
        
        if (_root.NandFolders.Get(out IEnumerable<string>? nandFolderPaths)) {
            const string infoKey = "Nand Folder(s)";

            foreach (string nandFolderPath in nandFolderPaths) {
                bool hasBaseGame = TkNandUtils.IsValid(keys, nandFolderPath, out bool hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGame, infoKey);
                report.SetHasUpdate(hasUpdateAsFile, infoKey);
            }
        }

    Result:
        return report.Build();
    }
}