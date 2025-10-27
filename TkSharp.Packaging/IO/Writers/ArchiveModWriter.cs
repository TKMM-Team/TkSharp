using System.IO.Compression;
using TkSharp.Core;
using TkSharp.Core.Extensions;

namespace TkSharp.Packaging.IO.Writers;

public sealed class ArchiveModWriter : ITkModWriter
{
    private string _root = string.Empty; 
    private readonly Dictionary<string, MemoryStream> _entries = [];
    
    public Stream OpenWrite(string filePath)
    {
        return _entries[Path.Combine(_root, filePath)] = new MemoryStream();
    }

    public void Compile(Stream output)
    {
        using ZipArchive writer = new(output, ZipArchiveMode.Create);

        foreach ((var fileName, var ms) in _entries.OrderBy(entry => entry.Key)) {
            using var entry = writer.CreateEntry(fileName.Replace('\\', '/')).Open();
            entry.Write(ms.GetSpan());
            ms.Dispose();
        }
    }

    public void SetRelativeFolder(string rootFolderPath)
    {
        _root = rootFolderPath;
    }
}