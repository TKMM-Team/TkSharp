using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using Path = LibHac.Fs.Path;

namespace TkSharp.Extensions.LibHac.IO;

public sealed class FatFileSystem : LocalFileSystem
{
    public static Result Create(out FatFileSystem fileSystem, string rootPath,
        PathMode pathMode = PathMode.DefaultCaseSensitivity, bool ensurePathExists = true)
    {
        UnsafeHelpers.SkipParamInit(out fileSystem);

        var localFs = new FatFileSystem();
        Result res = localFs.Initialize(rootPath, pathMode, ensurePathExists);
        if (res.IsFailure()) return res.Miss();

        fileSystem = localFs;
        return Result.Success;
    }

    protected override Result DoGetFileAttributes(out NxFileAttributes attributes, in Path path)
    {
        Result result = base.DoGetFileAttributes(out attributes, in path);
        ReadOnlySpan<byte> fileName = path.GetString();
        if (fileName.IndexOf((byte)'\0') is not (var nameEnd and > -1)) {
            return result;
        }
        
        fileName = fileName[..nameEnd];
        if (fileName.LastIndexOf((byte)'.') is not (var extSeparatorIndex and > -1)) {
            return result;
        }
        
        if (fileName[extSeparatorIndex..(extSeparatorIndex + 4)].SequenceEqual(".nca"u8)) {
            attributes |= NxFileAttributes.Archive;
        }

        return result;
    }

    protected override Result DoOpenDirectory(ref UniqueRef<IDirectory> outDirectory, in Path path, OpenDirectoryMode mode)
    {
        Result result = base.DoOpenDirectory(ref outDirectory, in path, mode);

        FatArchiveDirectory archiveDir = new(outDirectory.Get);
        outDirectory.Reset(archiveDir);

        return result;
    }
}