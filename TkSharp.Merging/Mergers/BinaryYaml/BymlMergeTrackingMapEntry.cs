using System.Collections;
using BymlLibrary;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public class BymlMergeTrackingMapEntry<TKey> : HashSet<TKey> where TKey : notnull;