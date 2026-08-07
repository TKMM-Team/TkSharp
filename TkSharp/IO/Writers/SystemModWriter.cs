using TkSharp.Core;

namespace TkSharp.IO.Writers;

public sealed class SystemModWriter : ITkModWriter
{
    private string _relativeRootFolder = string.Empty;
    private readonly string _rootFolder;

    public SystemModWriter(TkModManager manager, Ulid id)
    {
        _rootFolder = Path.Combine(manager.ModsFolderPath, id.ToString());

        if (!Directory.Exists(_rootFolder)) {
            return;
        }

        try {
            Directory.Delete(_rootFolder, recursive: true);
        }
        catch (Exception ex) {
            throw new IOException(
                $"Failed to delete content for mod ID '{id}'. Consider manually deleting the folder '{_rootFolder}' before attempting to install again.",
                ex);
        }
    }

    public Stream OpenWrite(string filePath)
    {
        var outputFilePath = Path.Combine(_rootFolder, _relativeRootFolder, filePath);
        if (Path.GetDirectoryName(outputFilePath) is { } folderPath) {
            Directory.CreateDirectory(folderPath);
        }
        
        return File.Create(outputFilePath);
    }

    public void SetRelativeFolder(string rootFolder)
    {
        _relativeRootFolder = rootFolder;
    }
}