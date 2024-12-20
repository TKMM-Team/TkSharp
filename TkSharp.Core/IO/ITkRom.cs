using CommunityToolkit.HighPerformance.Buffers;
using LibHac.Fs.Fsa;
using LibHac.Tools.FsSystem;
using TotkCommon;
using System.IO;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Tools.Fs;
using LibHac.Common.Keys;
using LibHac.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;

/// <summary>
/// Read Only Memory File System
/// </summary>
public interface IRomFS
{
    void Initialize();
    SpanOwner<byte> OpenRead(ReadOnlySpan<char> canonical);
}

public class RomFS : IRomFS
{
    private IFileSystem _fileSystem;
    private SwitchFs _switchFs;
    private Keyset _keySet;
    private const ulong ApplicationId = 0x0100F2C0115B6000;

    public void Initialize()
    {
        if (TotkConfig.Mode == FileSystemMode.Extracted)
        {
            _fileSystem = new LocalFileSystem(TotkConfig.GamePath);
            return;
        }

        if (TotkConfig.Mode == FileSystemMode.Packed)
        {
            LoadKeys();
            var baseFileFs = OpenPfs0(TotkConfig.BaseFilePath);
            IFileSystem updateFs = null;

            if (!string.IsNullOrEmpty(TotkConfig.UpdateFilePath))
            {
                updateFs = OpenPfs0(TotkConfig.UpdateFilePath);
            }

            var layeredFs = new LayeredFileSystem(new[] { baseFileFs, updateFs }.Where(fs => fs != null));
            _switchFs = SwitchFs.OpenNcaDirectory(_keySet, layeredFs);

            if (_switchFs.Applications.TryGetValue(ApplicationId, out var appInfo))
            {
                var nca = appInfo.UpdateNca ?? appInfo.MainNca;
                if (nca != null)
                {
                    _fileSystem = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);
                }
            }
        }

        if (_fileSystem == null)
        {
            throw new InvalidOperationException("Failed to initialize file system.");
        }
    }

    private static IFileSystem OpenPfs0(string filePath)
    {
        using (var storage = new LocalStorage(filePath, FileAccess.Read, FileMode.Open, FileShare.Read))
        {
            return new PartitionFileSystem(storage);
        }
    }

    public SpanOwner<byte> OpenRead(ReadOnlySpan<char> canonical)
    {
        if (_fileSystem == null)
        {
            throw new InvalidOperationException("File system not initialized.");
        }

        var path = canonical.ToString();
        if (!_fileSystem.OpenFile(out var file, path.ToU8Span(), OpenMode.Read).IsSuccess())
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        using (file) // Ensure the file handle is disposed properly
        {
            var buffer = SpanOwner<byte>.Allocate(file.GetSize());
            file.Read(out _, 0, buffer.Span).ThrowIfFailure(); // Ensure read operation is successful
            return buffer;
        }
    }

    private IFileSystem OpenStorage(string filePath)
    {
        var storage = new LocalStorage(filePath, FileAccess.Read);
        return new PartitionFileSystem(storage);
    }

    private void LoadKeys()
    {
        _keySet = new Keyset();
        ExternalKeyReader.ReadKeyFile(_keySet, TotkConfig.KeysDirectoryPath + "/prod.keys", TotkConfig.KeysDirectoryPath + "/title.keys", null, null);
    }
}

public enum FileSystemMode
{
    Extracted,
    Packed
}