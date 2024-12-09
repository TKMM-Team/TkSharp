using TkSharp.Core;

namespace TkSharp.IO.Writers;

public sealed class FolderModWriter(string outputModFolder) : ITkModWriter
{
    private string _relativeRootFolder = string.Empty;
    private readonly string _outputModFolder = outputModFolder;

    public Stream OpenWrite(string filePath)
    {
        string absolute = Path.Combine(_outputModFolder, _relativeRootFolder, filePath);

        if (Path.GetDirectoryName(absolute) is string folder) {
            Directory.CreateDirectory(folder);
        }

        return File.Create(absolute);
    }

    public void SetRelativeFolder(string rootFolder)
    {
        _relativeRootFolder = rootFolder;
    }
}