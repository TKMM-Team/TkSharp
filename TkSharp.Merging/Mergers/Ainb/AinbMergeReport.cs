namespace TkSharp.Merging.Mergers.Ainb;

public enum AinbMergeStatus { Merged, Unchanged, InputReused, Fallback }
public sealed record AinbConflict(string Source, string Path, string Kind);
public sealed record AinbNodeMatch(string Source, int VanillaIndex, int ModIndex, string Reason);
public sealed record AinbAdditionCount(string Source, int Retained, int ReachableFromCommands);

public sealed class AinbMergeReport
{
    public AinbMergeStatus Status { get; internal set; } = AinbMergeStatus.Merged;
    public string? Reason { get; internal set; }
    public string? SelectedSource { get; internal set; }
    public List<AinbConflict> Conflicts { get; } = [];
    public List<AinbNodeMatch> Matches { get; } = [];
    public List<AinbAdditionCount> Additions { get; } = [];
    public List<string> Warnings { get; } = [];
    public int NodeCount { get; internal set; }
    public int ReachableCount { get; internal set; }
}

public sealed class AinbMergeNotSupportedException(string message) : Exception(message);
