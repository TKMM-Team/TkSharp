using MessageStudio.Formats.BinaryText;
using Microsoft.Extensions.Logging;
using Revrs.Extensions;
using TkSharp.Core;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class MsbtChangelogBuilder : Singleton<MsbtChangelogBuilder>, ITkChangelogBuilder
{
    private static readonly MsbtOptions _options = new() {
        DuplicateKeyMode = MsbtDuplicateKeyMode.UseLastOccurrence
    };
    
    public bool CanProcessWithoutVanilla => false;
    
    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite)
    {
        if (srcBuffer.AsSpan().Read<ulong>() != Msbt.MAGIC) {
            TkLog.Instance.LogWarning("Expected MSBT file but found invalid magic: {CanonicalPath}", canonical);
            return false;
        }
        
        Msbt vanilla = Msbt.FromBinary(vanillaBuffer);

        Msbt changelog = [];
        Msbt src = Msbt.FromBinary(srcBuffer, _options);

        foreach ((string key, MsbtEntry entry) in src) {
            if (!vanilla.TryGetValue(key, out MsbtEntry? vanillaEntry)) {
                goto UpdateChangelog;
            }
            
            if (entry.Text == vanillaEntry.Text && entry.Attribute == vanillaEntry.Attribute) {
                continue;
            }
            
        UpdateChangelog:
            changelog[key] = entry;
        }

        if (changelog.Count == 0) {
            return false;
        }

        using Stream output = openWrite(path, canonical);
        changelog.WriteBinary(output);
        return true;
    }

    public void Dispose()
    {
    }
}