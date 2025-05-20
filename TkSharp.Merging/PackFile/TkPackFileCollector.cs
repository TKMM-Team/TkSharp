using System.Collections.Concurrent;
using System.Diagnostics;
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
        foreach (Either<PackFileEntry, PackFileDelayMergeEntry> either in entries) {
            string name = either.Match(r => r.Changelog.Canonical, l => l.Changelog.Canonical);
            
            either.Match(
                entry => {
                    using MemoryStream output = new();
                    
                    if (!sarc.TryGetValue(name, out ArraySegment<byte> baseData)) {
                        _merger.MergeCustomTarget(entry.Merger, entry.Inputs[0], entry.Inputs.AsSpan(1..), entry.Changelog, output);
                        goto UpdateSarc;
                    }

                    if (PackMerger.IsRemovedEntry(baseData)) {
                        sarc.Remove(name);
                        return;
                    }

                    using (RentedBuffers<byte> inputs = RentedBuffers<byte>.Allocate(entry.Inputs)) {
                        entry.Merger.Merge(entry.Changelog, inputs, baseData, output);
                    }
                    
                UpdateSarc:
                    foreach (Stream input in entry.Inputs) {
                        input.Seek(0, SeekOrigin.Begin);
                    }
                    
                    ArraySegment<byte> buffer = output.GetSpan();
                    sarc[name] = buffer;
                    resourceSizeCollector.Collect(buffer.Count, name, buffer);
                },
                entry => {
                    if (sarc.TryGetValue(name, out ArraySegment<byte> entryData) && PackMerger.IsRemovedEntry(entryData)) {
                        sarc.Remove(name);
                        return;
                    }
                    
                    if (entry.Data is MemoryStream msData) {
                        ArraySegment<byte> buffer = msData.GetSpan();
                        sarc[name] = buffer;
                        resourceSizeCollector.Collect(buffer.Count, name, buffer);
                        return;
                    }

                    using (Stream sarcEntry = sarc.OpenWrite(name)) {
                        entry.Data.CopyTo(sarcEntry);
                        entry.Data.Seek(0, SeekOrigin.Begin);
                    }

                    resourceSizeCollector.Collect(sarc[name].Count, name, sarc[name]);
                }
            );   
        }

        using MemoryStream ms = new();
        sarc.Write(ms);
        _merger.CopyMergedToSimpleOutput(ms, relativePath, key.Attributes, key.ZsDictionaryId);
    }

    public void Collect(TkChangelogEntry changelog, Stream input)
    {
        foreach (string archiveCanonical in changelog.ArchiveCanonicals) {
            _cache.Add(new PackFileEntry(
                new PackFileEntryKey(archiveCanonical, changelog.Attributes, changelog.ZsDictionaryId),
                changelog, input)
            );
        }
    }

    public void Collect(TkChangelogEntry changelog, ITkMerger merger, Stream[] streams)
    {
        foreach (string archiveCanonical in changelog.ArchiveCanonicals) {
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