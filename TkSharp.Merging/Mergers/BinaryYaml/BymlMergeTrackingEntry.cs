using BymlLibrary;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public class BymlMergeTrackingEntry
{
    public string? ArrayName { get; set; }
    
    public int Depth { get; set; }
    
    public List<List<(int InsertIndex, Byml Entry)>> Additions { get; } = [];

    public HashSet<int> Removals { get; } = [];

    public Dictionary<object, int> KeyedRemovals { get; } = [];
}