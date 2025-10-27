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

        if (_root.ExtractedGameDumpFolderPath.Get(out var gameDumpPaths)) {
            const string infoKey = "Game Dump Path(s)";
            foreach (var gameDumpPath in gameDumpPaths) {
                var hasBaseGame = TkGameDumpUtils.CheckGameDump(gameDumpPath, out var hasUpdate);
                report.SetHasBaseGame(hasBaseGame, infoKey);
                report.SetHasUpdate(hasUpdate, infoKey);
            }
        }

        if (!TkKeyUtils.TryGetKeys(out var keys)) {
            goto Result;
        }
        
        report.SetKeys(keys);

        if (_root.SdCard.Get(out var sdCardFolderPath)) {
            const string infoKey = "Installed or Dumped on SD Card";
            var hasBaseGameInSdCard = TkSdCardUtils.CheckSdCard(keys, sdCardFolderPath, out var hasUpdateInSdCard);
            report.SetHasBaseGame(hasBaseGameInSdCard, infoKey);
            report.SetHasUpdate(hasUpdateInSdCard, infoKey);
        }

        if (_root.PackagedBaseGame.Get(out var packagedBaseGamePaths)) {
            const string packagedInfoKey = "Packaged Base Game File(s)";
            const string splitFileInfoKey = "Packaged Base Game Split File(s)";

            foreach (var packagedBaseGamePath in packagedBaseGamePaths) {
                var hasBaseGameAsFile = TkGameRomUtils.IsFileValid(keys, packagedBaseGamePath, out var hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGameAsFile, packagedInfoKey);
                report.SetHasUpdate(hasUpdateAsFile, packagedInfoKey);
                
                var hasBaseGameAsSplitFile = TkGameRomUtils.IsSplitFileValid(keys, packagedBaseGamePath, out var hasUpdateAsSplitFile);
                report.SetHasBaseGame(hasBaseGameAsSplitFile, splitFileInfoKey);
                report.SetHasUpdate(hasUpdateAsSplitFile, splitFileInfoKey);
            }
        }

        if (_root.PackagedUpdate.Get(out var packagedUpdatePaths)) {
            const string packagedInfoKey = "Packaged Update File(s)";
            const string splitFileInfoKey = "Packaged Update Split File(s)";
            
            foreach (var packagedUpdatePath in packagedUpdatePaths) {
                var hasBaseGameAsFile = TkGameRomUtils.IsFileValid(keys, packagedUpdatePath, out var hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGameAsFile, packagedInfoKey);
                report.SetHasUpdate(hasUpdateAsFile, packagedInfoKey);
                
                var hasBaseGameAsSplitFile = TkGameRomUtils.IsSplitFileValid(keys, packagedUpdatePath, out var hasUpdateAsSplitFile);
                report.SetHasBaseGame(hasBaseGameAsSplitFile, splitFileInfoKey);
                report.SetHasUpdate(hasUpdateAsSplitFile, splitFileInfoKey);
            }
        }
        
        if (_root.NandFolders.Get(out var nandFolderPaths)) {
            const string infoKey = "Nand Folder(s)";

            foreach (var nandFolderPath in nandFolderPaths) {
                var hasBaseGame = TkNandUtils.IsValid(keys, nandFolderPath, out var hasUpdateAsFile);
                report.SetHasBaseGame(hasBaseGame, infoKey);
                report.SetHasUpdate(hasUpdateAsFile, infoKey);
            }
        }

    Result:
        return report.Build();
    }
}