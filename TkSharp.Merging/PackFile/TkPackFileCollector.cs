using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using LanguageExt;
using SarcLibrary;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;
using TkSharp.Merging.Mergers;
using TkSharp.Merging.ResourceSizeTable;

namespace TkSharp.Merging.PackFile;

public sealed class TkPackFileCollector(TkMerger merger, TkResourceSizeCollector resourceSizeCollector, ITkRom rom)
{
    private readonly ConcurrentBag<Either<PackFileEntry, PackFileDelayMergeEntry>> _cache = [];
    private readonly ConcurrentDictionary<string, Sarc> _trackedArchives = [];
    private readonly TkMerger _merger = merger;

    public void Write()
    {
        foreach (IGrouping<PackFileEntryKey, Either<PackFileEntry, PackFileDelayMergeEntry>> packFile in _cache.GroupBy(x => x.Match(r => r.Key, l => l.Key), x => x)) {
            PackFileEntryKey key = packFile.Key;
            string relativePath = rom.CanonicalToRelativePath(key.ArchiveCanonical, key.Attributes);

            if (_trackedArchives.TryGetValue(key.ArchiveCanonical, out Sarc? trackedSarc)) {
                WritePackFile(trackedSarc, relativePath, key, packFile);
                continue;
            }

            using RentedBuffer<byte> vanilla = rom.GetVanilla(relativePath);
            Sarc sarc = vanilla.IsEmpty ? new Sarc() : Sarc.FromBinary(vanilla.Segment);
            WritePackFile(sarc, relativePath, key, packFile);
        }

        foreach (Either<PackFileEntry,PackFileDelayMergeEntry> either in _cache) {
            either.Match(
                entry => entry.Inputs.Iter(x => x.Dispose()),
                entry => entry.Data.Dispose()
            );
        }
    }

    private void WritePackFile(Sarc sarc, string relativePath, PackFileEntryKey key,
        IEnumerable<Either<PackFileEntry, PackFileDelayMergeEntry>> entries)
    {
        // Merge once per nested file to avoid duplicate appends
        var groups = entries.GroupBy(e => e.Match(r => r.Changelog.Canonical, l => l.Changelog.Canonical));

        foreach (var group in groups) {
            string name = group.Key;

            if (sarc.TryGetValue(name, out ArraySegment<byte> existingData) && PackMerger.IsRemovedEntry(existingData)) {
                sarc.Remove(name);
                continue;
            }

            // Collect inputs once (prefer delay-merge entries if present to avoid duplicates)
            List<ArraySegment<byte>> inputs = new();
            ITkMerger? merger = _merger.GetMerger(name);
            TkChangelogEntry changelog = group.First().Match(left => left.Changelog, right => right.Changelog);

            bool hasDelay = group.Any(e => e.IsRight);
            if (hasDelay) {
                foreach (Either<PackFileEntry, PackFileDelayMergeEntry> either in group) {
                    if (either.IsRight) {
                        PackFileDelayMergeEntry right = either.RightToList()[0];
                        foreach (Stream s in right.Inputs) {
                            using MemoryStream tmp = new();
                            s.CopyTo(tmp);
                            s.Seek(0, SeekOrigin.Begin);
                            inputs.Add(tmp.ToArray());
                        }
                    }
                }
            }
            else {
                foreach (Either<PackFileEntry, PackFileDelayMergeEntry> either in group) {
                    if (either.IsLeft) {
                        PackFileEntry left = either.LeftToList()[0];
                        using MemoryStream tmp = new();
                        left.Data.CopyTo(tmp);
                        left.Data.Seek(0, SeekOrigin.Begin);
                        inputs.Add(tmp.ToArray());
                    }
                }
            }

            if (inputs.Count == 0) { continue; }

            // Deduplicate identical changelog buffers to avoid applying same changelog twice
            if (inputs.Count > 1) {
                var seen = new System.Collections.Generic.HashSet<int>();
                var deduped = new List<ArraySegment<byte>>(inputs.Count);
                foreach (ArraySegment<byte> seg in inputs) {
                    // simple hash: length xor first/last bytes; fast and sufficient to weed duplicates here
                    int hash = seg.Count;
                    if (seg.Count > 0) {
                        hash ^= seg.Array![seg.Offset];
                        hash = (hash << 5) - hash + seg.Array![seg.Offset + seg.Count - 1];
                    }
                    if (!seen.Add(hash)) {
                        continue;
                    }
                    deduped.Add(seg);
                }
                inputs = deduped;
            }

            // If no merger, last-wins
            if (merger is null) {
                ArraySegment<byte> last = inputs[^1];
                sarc[name] = last;
                resourceSizeCollector.Collect(last.Count, name, last);
                continue;
            }

            using MemoryStream mergedOut = new();
            if (sarc.TryGetValue(name, out ArraySegment<byte> baseData)) {
                if (inputs.Count == 1) {
                    merger.MergeSingle(changelog, inputs[0], baseData, mergedOut);
                }
                else {
                    merger.Merge(changelog, inputs, baseData, mergedOut);
                }
            }
            else {
                if (inputs.Count == 1) {
                    // No base; single input → write as-is
                    ArraySegment<byte> single = inputs[0];
                    sarc[name] = single;
                    resourceSizeCollector.Collect(single.Count, name, single);
                    continue;
                }
                using MemoryStream baseStream = new(inputs[0].ToArray(), writable: false);
                Stream[] others = inputs.Skip(1).Select(seg => (Stream)new MemoryStream(seg.ToArray(), writable: false)).ToArray();
                try {
                    _merger.MergeCustomTarget(merger, baseStream, others, changelog, mergedOut);
                }
                finally {
                    foreach (Stream s in others) s.Dispose();
                }
            }

            ArraySegment<byte> buffer = mergedOut.GetSpan();
            sarc[name] = buffer;
            resourceSizeCollector.Collect(buffer.Count, name, buffer);
        }

        using MemoryStream ms = new();
        sarc.Write(ms);
        _merger.CopyMergedToSimpleOutput(ms, relativePath, key.Attributes, key.ZsDictionaryId);
    }

    public void Collect(TkChangelogEntry changelog, Stream input)
    {
        foreach (string archiveCanonical in changelog.RuntimeArchiveCanonicals) {
            _cache.Add(new PackFileEntry(
                new PackFileEntryKey(archiveCanonical, changelog.Attributes, changelog.ZsDictionaryId),
                changelog, input)
            );
        }
    }

    public void Collect(TkChangelogEntry changelog, ITkMerger merger, Stream[] streams)
    {
        foreach (string archiveCanonical in changelog.RuntimeArchiveCanonicals) {
            _cache.Add(new PackFileDelayMergeEntry(
                new PackFileEntryKey(archiveCanonical, changelog.Attributes, changelog.ZsDictionaryId),
                changelog, merger, streams)
            );
        }
    }

    public void RegisterPackFile(string canonical, Sarc sarc)
    {
        _trackedArchives[canonical] = sarc;
    }

    private readonly struct PackFileEntry(PackFileEntryKey key, TkChangelogEntry changelog, Stream data)
    {
        public readonly PackFileEntryKey Key = key;
        public readonly TkChangelogEntry Changelog = changelog;
        public readonly Stream Data = data;
    }

    private readonly struct PackFileDelayMergeEntry(PackFileEntryKey key, TkChangelogEntry changelog, ITkMerger merger, Stream[] inputs)
    {
        public readonly PackFileEntryKey Key = key;
        public readonly TkChangelogEntry Changelog = changelog;
        public readonly ITkMerger Merger = merger;
        public readonly Stream[] Inputs = inputs;
    }

    private readonly struct PackFileEntryKey(string archiveCanonical, TkFileAttributes attributes, int zsDictionaryId) : IEquatable<PackFileEntryKey>
    {
        public readonly string ArchiveCanonical = archiveCanonical;
        public readonly TkFileAttributes Attributes = attributes;
        public readonly int ZsDictionaryId = zsDictionaryId;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is not PackFileEntryKey packFileEntry) {
                return false;
            }

            return packFileEntry.ArchiveCanonical == ArchiveCanonical;
        }

        public bool Equals(PackFileEntryKey other)
        {
            return ArchiveCanonical == other.ArchiveCanonical;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ArchiveCanonical);
        }
    }
}