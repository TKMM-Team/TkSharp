using TkSharp.Core;

namespace TkSharp.IO.Writers;

public sealed class FolderModWriter(string outputModFolder) : ITkModWriter
{
    private readonly string _outputModFolder = outputModFolder;

    public Stream OpenWrite(string filePath)
    {
        string absolute = Path.Combine(_outputModFolder, filePath);

        if (Path.GetDirectoryName(absolute) is string folder) {
            Directory.CreateDirectory(folder);
        }

        return File.Create(absolute);
    }
}