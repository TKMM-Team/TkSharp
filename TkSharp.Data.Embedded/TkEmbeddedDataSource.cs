using System.Diagnostics.CodeAnalysis;

namespace TkSharp.Data.Embedded;

public static class TkEmbeddedDataSource
{
    /// <summary>
    /// Returns the stream to an embedded file, or null if the file cannot not be found.
    /// </summary>
    /// <param name="path">The path to an embedded asset</param>
    /// <returns></returns>
    public static Stream? GetResource(string path)
    {
        return typeof(TkEmbeddedDataSource)
            .Assembly
            .GetManifestResourceStream($"TkSharp.Data.Embedded.{path.Replace('/', '.')}");
    }
    
    public static bool TryGetResource(string path, [MaybeNullWhen(false)] out Stream stream)
    {
        if ((stream = GetResource(path)) is not null) {
            return true;
        }

        return false;
    }

    public static Stream GetChecksumsBin()
    {
        return GetResource("Resources/Checksums.bin")!;
    }

    public static Stream GetPackFileLookup()
    {
        return GetResource("Resources/PackFileLookup.pkcache.zs")!;
    }
}