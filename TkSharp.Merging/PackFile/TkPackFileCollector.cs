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
        foreach (PackFileEntry entry in entries) {
            string name = entry.Changelog.Canonical;
            if (sarc.TryGetValue(name, out ArraySegment<byte> entryData) && PackMerger.IsRemovedEntry(entryData)) {
                sarc.Remove(name);
                continue;
            }
                    
            if (entry.Data is MemoryStream msData) {
                ArraySegment<byte> buffer = msData.GetSpan();
                sarc[name] = buffer;
                resourceSizeCollector.Collect(buffer.Count, name, buffer);
                continue;
            }

            using (Stream sarcEntry = sarc.OpenWrite(name)) {
                if (entry.Data is not null) {
                    entry.Data.CopyTo(sarcEntry);
                    entry.Data.Seek(0, SeekOrigin.Begin);
                }
                else {
                    sarcEntry.Write(entry.Buffer);
                }
            }

            resourceSizeCollector.Collect(sarc[name].Count, name, sarc[name]);
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