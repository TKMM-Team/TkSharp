using TkSharp.Core;

namespace TkSharp.Merging;
 
public delegate Stream OpenWriteChangelog(TkPath path, string canonical, string? archiveCanonical = null);

public interface ITkChangelogBuilder
{
    /// <summary>
    /// True if this changelog builder can process
    /// a changelog without a vanilla file buffer.
    /// </summary>
    public bool CanProcessWithoutVanilla { get; }
    
    /// <summary>
    /// Builds a changelog of the provided target.
    /// </summary>
    /// <param name="canonical"></param>
    /// <param name="path"></param>
    /// <param name="flags"></param>
    /// <param name="srcBuffer"></param>
    /// <param name="vanillaBuffer"></param>
    /// <param name="openWrite"></param>
    /// <returns>False if no changes were detected in the target</returns>
    bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer,
        OpenWriteChangelog openWrite);
}