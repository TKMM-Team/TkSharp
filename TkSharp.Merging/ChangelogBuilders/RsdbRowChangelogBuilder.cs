using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BymlLibrary;
using BymlLibrary.Nodes.Containers;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Revrs;
using TkSharp.Core;
using TkSharp.Merging.ChangelogBuilders.BinaryYaml;
using TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

namespace TkSharp.Merging.ChangelogBuilders;

public static class RsdbRowChangelogBuilder
{
    public static readonly RsdbRowChangelogBuilder<string> RowId = new("__RowId");
    public static readonly RsdbRowChangelogBuilder<string> Name = new("Name");
    public static readonly RsdbRowChangelogBuilder<string> FullTagId = new("FullTagId");
    public static readonly RsdbRowChangelogBuilder<uint> NameHash = new("NameHash");
}

public sealed class RsdbRowChangelogBuilder<TKey>(string keyName) : ITkChangelogBuilder where TKey : notnull
{
    private readonly string _keyName = keyName;
    
    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        ulong dbNameHash = GetDbNameHash(path);

        Dictionary<TKey, Byml> changelog = [];
        HashSet<TKey> foundKeys = [];
        
        var root = Byml.FromBinary(srcBuffer, out var endianness, out ushort version);
        var rows = root.GetArray();

        var vanillaRows = Byml.FromBinary(vanillaBuffer).GetArray();

        BymlTrackingInfo bymlTrackingInfo = new(path.Canonical, depth: 0);

        for (int i = 0; i < rows.Count; i++) {
            var rowEntry = rows[i];
            var entry = rowEntry.GetMap();

            if (!TryGetKeyHash(entry, out ulong keyHash, out var key)) {
                TkLog.Instance.LogWarning(
                    "RSDB file '{Canonical}' has an invalid entry at {Index}. The key field '{KeyName}' is missing.",
                    canonical, i, _keyName);
                continue;
            }
            
            foundKeys.Add(key);
            
            if (!RsdbRowIndex.TryGetIndex(dbNameHash, keyHash, out int index)) {
                goto UpdateChangelog;
            }

            if (!RsdbRowCache.TryGetVanilla(dbNameHash, keyHash, path.FileVersion, out var vanillaRow)) {
                vanillaRow = vanillaRows[index];
            }
            
            if (BymlChangelogBuilder.LogChangesInline(ref bymlTrackingInfo, ref rowEntry, vanillaRow)) {
                continue;
            }
            
        UpdateChangelog:
            changelog[key] = rowEntry;
        }

        if (flags.TrackRemovedRsDbEntries) {
            for (int i = 0; i < vanillaRows.Count; i++) {
                var rowEntry = vanillaRows[i];
                var entry = rowEntry.GetMap();
                
                if (!TryGetKeyHash(entry, out ulong _, out var key)) {
                    TkLog.Instance.LogWarning(
                        "Vanilla RSDB file '{Canonical}' has an invalid entry at {Index}. The key field '{KeyName}' is missing.",
                        canonical, i, _keyName);
                    continue;
                }

                if (foundKeys.Contains(key)) {
                    continue;
                }
                
                changelog[key] = BymlChangeType.Remove;
            }
        }

        if (changelog.Count is 0) {
            return false;
        }

        using MemoryStream ms = new();
        var changelogByml = changelog switch {
            IDictionary<uint, Byml> hashMap32 => new Byml(hashMap32),
            IDictionary<string, Byml> map => new Byml(map),
            _ => throw new NotSupportedException(
                $"The type '{typeof(TKey)}' is not a supported RSDB map type.")
        };
        
        changelogByml.WriteBinary(ms, endianness, version);
        ms.Seek(0, SeekOrigin.Begin);
        
        using var output = openWrite(path, canonical);
        ms.CopyTo(output);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetKeyHash(in BymlMap row, out ulong keyHash, [MaybeNullWhen(false)] out TKey key)
    {
        if (!row.TryGetValue(_keyName, out var keyEntry)) {
            keyHash = 0;
            key = default;
            return false;
        }

        keyHash = keyEntry.Value switch {
            string stringKey => XxHash3.HashToUInt64(stringKey.AsSpan().Cast<char, byte>()),
            uint uInt32 => uInt32,
            _ => throw new NotSupportedException(
                $"The BYML type '{keyEntry.Type}' is not a supported RSDB key type."),
        };
        
        key = keyEntry.Get<TKey>();
        
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetDbNameHash(in TkPath path)
    {
        return XxHash3.HashToUInt64(MemoryMarshal.Cast<char, byte>(path.Canonical));
    }

    public void Dispose()
    {
    }
}