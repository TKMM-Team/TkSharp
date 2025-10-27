using TkSharp.Core;

namespace TkSharp.IO.Writers;

public sealed class SystemModWriter(TkModManager manager, Ulid id) : ITkModWriter
{
    private string _relativeRootFolder = string.Empty;
    private readonly string _rootFolder = Path.Combine(manager.ModsFolderPath, id.ToString());
    
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