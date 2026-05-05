using LibHac;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.Ncm;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using LibHac.Util;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using Path = LibHac.Fs.Path;

namespace TkSharp.Extensions.LibHac.Util;

internal static class TkSwitchFs
{
    public static bool TryOpenSdCard(
        KeySet keySet,
        ref UniqueRef<IAttributeFileSystem> fileSystem,
        string sdRootPath,
        out SwitchFs? switchFs,
        out IDisposable? cleanup)
    {
        switchFs = null;
        cleanup = null;
        NcaProbeFileSystem? probeFs = null;

        try {
            ConcatenationFileSystem concatFs = new(ref fileSystem);

            using Path contentDirPath = new();
            PathFunctions.SetUpFixedPath(ref contentDirPath.Ref(), "/Nintendo/Contents"u8).ThrowIfFailure();

            SubdirectoryFileSystem contentDirFs = new(concatFs);
            contentDirFs.Initialize(in contentDirPath).ThrowIfFailure();

            AesXtsFileSystem encContentFs = new(contentDirFs, keySet.SdCardEncryptionKeys[1].DataRo.ToArray(), 0x4000);

            using SharedRef<IFileSystem> encContentRef = new(encContentFs);
            probeFs = new NcaProbeFileSystem(keySet, encContentRef, sdRootPath);
            probeFs.BuildTotkAllowList();

            switchFs = SwitchFs.OpenNcaDirectory(keySet, probeFs);
            cleanup = probeFs;
            probeFs = null;
            return true;
        }
        catch (Exception ex) {
            TkLog.Instance.LogWarning(ex,
                "Failed to create filtered SD content view for '{SdRootPath}'.",
                sdRootPath);

            probeFs?.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Wraps the SD <c>Nintendo/Contents</c> filesystem, filters out non-TotK entries,
    /// and ignores individual <c>.nca</c> entries that cannot be opened/parsed.
    /// </summary>
    private sealed class NcaProbeFileSystem(KeySet keys, SharedRef<IFileSystem> baseFileSystem, string sdRootPath)
        : ForwardingFileSystem(in baseFileSystem)
    {
        private readonly Lock _totkLock = new();
        private readonly HashSet<string> _totkNcaIds = new(StringComparer.OrdinalIgnoreCase);

        protected override Result DoOpenFile(ref UniqueRef<IFile> outFile, ref readonly Path path, OpenMode mode)
        {
            var isNcaPath = path.ToString().EndsWith(".nca", StringComparison.OrdinalIgnoreCase);
            var pathString = isNcaPath ? path.ToString() : string.Empty;
            var hasNcaId = TryGetNcaId(pathString, out var ncaId, out var isCnmtPath);
            var isKnownTotkNca = hasNcaId && IsTotkNcaId(ncaId);

            try {
                if (isNcaPath && !isKnownTotkNca) {
                    return ResultFs.PathNotFound;
                }

                var res = base.DoOpenFile(ref outFile, in path, mode);
                if (res.IsFailure()) {
                    return res;
                }

                if (!isNcaPath) {
                    return Result.Success;
                }

                if (!hasNcaId) {
                    outFile.Destroy();
                    return ResultFs.PathNotFound;
                }

                var nca = new Nca(keys, outFile.Get.AsStorage());
                
                if (isCnmtPath || ShouldIncludeNca(nca)) {
                    return Result.Success;
                }
                
                outFile.Destroy();
                return ResultFs.PathNotFound;

            }
            catch (HorizonResultException ex) when (isNcaPath) {
                if (!isKnownTotkNca) {
                    outFile.Destroy();
                    return ResultFs.PathNotFound;
                }

                TkLog.Instance.LogWarning(ex,
                    "Skipping unreadable NCA '{NcaPath}' while scanning '{SdRootPath}'.",
                    pathString, sdRootPath);

                outFile.Destroy();
                return ResultFs.PathNotFound;
            }
            catch (Exception ex) {
                if (!isNcaPath) {
                    throw;
                }

                if (isKnownTotkNca) {
                    TkLog.Instance.LogWarning(ex,
                        "Skipping unreadable NCA '{NcaPath}' while scanning '{SdRootPath}'.",
                        pathString, sdRootPath);
                }

                outFile.Destroy();
                return ResultFs.PathNotFound;
            }
        }

        public void BuildTotkAllowList()
        {
            var totkCnmtCount = 0;
            foreach (var entry in BaseFileSystem.Get.EnumerateEntries("*.nca", SearchOptions.RecurseSubdirectories)
                         .Where(x => x.Type == DirectoryEntryType.File)) {
                if (!TryGetNcaId(entry.FullPath, out var ncaId, out _)) {
                    continue;
                }

                try {
                    using UniqueRef<IFile> file = new();
                    BaseFileSystem.Get.OpenFile(ref file.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

                    var nca = new Nca(keys, file.Get.AsStorage());
                    if (nca.Header.ContentType != NcaContentType.Meta) {
                        continue;
                    }

                    if (TryRegisterTotkCnmt(nca, ncaId)) {
                        totkCnmtCount++;
                    }
                }
                catch {
                    // Ignore broken/foreign NCAs while discovering TotK allowlist.
                }
            }

            lock (_totkLock) {
                TkLog.Instance.LogDebug(
                    "[ROM *] [SD Card] TotK CNMT allowlist build complete. Found {TotkCnmtCount} TotK CNMT(s), {TotkNcaIdCount} TotK NCA id(s).",
                    totkCnmtCount, _totkNcaIds.Count);
            }
        }

        private static bool ShouldIncludeNca(Nca nca)
        {
            return nca.Header.ContentType is NcaContentType.Meta
                or NcaContentType.Program
                or NcaContentType.Data
                or NcaContentType.Control;
        }

        private bool TryRegisterTotkCnmt(Nca cnmtNca, string metaNcaId)
        {
            if (cnmtNca.Header.ContentType != NcaContentType.Meta) {
                return false;
            }

            using var fs = cnmtNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
            var cnmtPath = fs.EnumerateEntries("/", "*.cnmt").Single().FullPath;

            using UniqueRef<IFile> file = new();
            fs.OpenFile(ref file.Ref, cnmtPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

            Cnmt metadata = new(file.Release().AsStream());
            if (metadata.ApplicationTitleId != TkGameRomUtils.EX_KING_APP_ID) {
                return false;
            }

            lock (_totkLock) {
                _totkNcaIds.Add(metaNcaId);
                foreach (var content in metadata.ContentEntries) {
                    _totkNcaIds.Add(content.NcaId.ToHexString());
                }
            }

            TkLog.Instance.LogDebug(
                "[ROM *] [SD Card] Registered TotK CNMT {MetaNcaId} with {ContentCount} content entrie(s).",
                metaNcaId, metadata.ContentEntries.Length);
            return true;
        }

        private bool IsTotkNcaId(string ncaId)
        {
            lock (_totkLock) {
                return _totkNcaIds.Contains(ncaId);
            }
        }

        private static bool TryGetNcaId(string pathOrName, out string ncaId, out bool isCnmt)
        {
            var fileName = System.IO.Path.GetFileName(pathOrName);
            
            if (fileName.EndsWith(".cnmt.nca", StringComparison.OrdinalIgnoreCase)) {
                ncaId = fileName[..^".cnmt.nca".Length];
                isCnmt = true;
                return !string.IsNullOrWhiteSpace(ncaId);
            }

            if (fileName.EndsWith(".nca", StringComparison.OrdinalIgnoreCase)) {
                ncaId = fileName[..^".nca".Length];
                isCnmt = false;
                return !string.IsNullOrWhiteSpace(ncaId);
            }

            ncaId = string.Empty;
            isCnmt = false;
            return false;
        }
    }
}
