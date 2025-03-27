using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp.Merging;

public interface ITkOptimizerBuilder
{
    /// <summary>
    /// Builds metadata for the provided target without handling romfs content.
    /// </summary>
    /// <param name="source">The mod source to process</param>
    /// <param name="writer">The output writer</param>
    /// <param name="systemSource">Optional system source information</param>
    /// <returns>The built changelog containing only metadata</returns>
    ValueTask<TkChangelog> BuildMetadataAsync(ITkModSource source, ITkModWriter writer, ITkSystemSource? systemSource = null);
}