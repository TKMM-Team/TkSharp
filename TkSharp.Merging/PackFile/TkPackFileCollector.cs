using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
    private readonly ConcurrentBag<PackFileEntry> _cache = [];
    private readonly ConcurrentDictionary<string, Sarc> _trackedArchives = [];
    private readonly TkMerger _merger = merger;

    public void Write()
    {
        foreach (IGrouping<PackFileEntryKey, PackFileEntry> packFile in _cache.GroupBy(x => x.Key, x => x)) {
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

        foreach (PackFileEntry entry in _cache) {
            entry.Data?.Dispose();
        }
    }

    private void WritePackFile(Sarc sarc, string relativePath, PackFileEntryKey key,
        IEnumerable<PackFileEntry> entries)
    {
        foreach (IGrouping<string, PackFileEntry> group in entries.GroupBy(e => e.Changelog.Canonical)) {
            string name = group.Key;

            if (sarc.TryGetValue(name, out ArraySegment<byte> existingData) && PackMerger.IsRemovedEntry(existingData)) {
                sarc.Remove(name);
                continue;
            }

            // Resolve merger for nested file
            ITkMerger? merger = _merger.GetMerger(name);

            // Collect inputs (dedup identical buffers)
            List<ArraySegment<byte>> inputs = new();
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (PackFileEntry e in group) {
                ArraySegment<byte> seg;
                if (e.Data is MemoryStream ms) {
                    seg = ms.GetSpan();
                }
                else if (e.Data is not null) {
                    using MemoryStream tmp = new();
                    e.Data.CopyTo(tmp);
                    e.Data.Seek(0, SeekOrigin.Begin);
                    seg = tmp.ToArray();
                }
                else {
                    seg = e.Buffer!;
                }

                int h = seg.Count;
                if (seg.Count > 0 && seg.Array is not null) {
                    h ^= seg.Array[seg.Offset];
                    h = (h << 5) - h + seg.Array[seg.Offset + seg.Count - 1];
                }
                if (seen.Add(h)) {
                    inputs.Add(seg);
                }
            }

            if (inputs.Count == 0) {
                continue;
            }
            
            inputs.Reverse();

            // If no merger for this type, last-wins copy
            if (merger is null) {
                ArraySegment<byte> last = inputs[^1];
                sarc[name] = last;
                resourceSizeCollector.Collect(last.Count, name, last);
                continue;
            }

            using MemoryStream output = new();

            if (sarc.TryGetValue(name, out ArraySegment<byte> baseData) && baseData.Array is not null) {
                if (inputs.Count == 1) {
                    merger.MergeSingle(group.First().Changelog, inputs[0], baseData, output);
                }
                else {
                    merger.Merge(group.First().Changelog, inputs, baseData, output);
                }
            }
            else {
                // No base inside pack; copy last input
                ArraySegment<byte> last = inputs[^1];
                sarc[name] = last;
                resourceSizeCollector.Collect(last.Count, name, last);
                continue;
            }

            ArraySegment<byte> buffer = output.GetSpan();
            sarc[name] = buffer;
            resourceSizeCollector.Collect(buffer.Count, name, buffer);
        }

        using MemoryStream mstream = new();
        sarc.Write(mstream);
        _merger.CopyMergedToSimpleOutput(mstream, relativePath, key.Attributes, key.ZsDictionaryId);
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

    public void Collect(TkChangelogEntry changelog, byte[] input)
    {
        foreach (string archiveCanonical in changelog.RuntimeArchiveCanonicals) {
            _cache.Add(new PackFileEntry(
                new PackFileEntryKey(archiveCanonical, changelog.Attributes, changelog.ZsDictionaryId),
                changelog, null, input)
            );
        }
    }

    public void RegisterPackFile(string canonical, Sarc sarc)
    {
        _trackedArchives[canonical] = sarc;
    }

    private readonly struct PackFileEntry(PackFileEntryKey key, TkChangelogEntry changelog, Stream? data = null, byte[]? buffer = null)
    {
        public readonly PackFileEntryKey Key = key;
        public readonly TkChangelogEntry Changelog = changelog;
        public readonly Stream? Data = data;
        public readonly byte[]? Buffer = buffer;
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