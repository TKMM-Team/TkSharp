namespace TkSharp.Core;

/// <summary>
/// Provides an interface for reading system files.
/// </summary>
public interface ITkSystemSource
{
    Stream OpenRead(string relativeFilePath);
    
    bool Exists(string relativeFilePath);

    ITkSystemSource GetRelative(string relativeSourcePath);

    IEnumerable<string> EnumerateRomfsBuckets() => [];
}