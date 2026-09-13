using Tkmm.AalSharp.Bars;
using TkSharp.Core;
using TkSharp.Merging.Common.AudioResource;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class BarsChangelogBuilder : ITkChangelogBuilder
{
    internal static readonly byte[] IsVanillaMark = [.. "TKCLVNLA"u8];
    
    public static readonly BarsChangelogBuilder Instance = new();

    public bool CanProcessWithoutVanilla => false;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite, int gameVersion)
    {
        var changelog = CreateChangelog(flags, srcBuffer, vanillaBuffer);

        if (changelog.Count == 0) {
            return false;
        }

        using var output = openWrite(path, canonical);
        changelog.Write(output);
        
        return true;
    }

    public static AudioResource CreateChangelog(in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer, ArraySegment<byte> vanillaBuffer)
    {
        var changelog = new AudioResource();
        var src = AudioResource.FromBinary(srcBuffer);
        var vanilla = AudioResource.FromBinary(vanillaBuffer);

        foreach (var (key, entry) in src) {
            if (!vanilla.TryGetValue(key, out var vanillaEntry)) {
                changelog.Add(key, new AudioResourceAsset {
                    Metadata = entry.Metadata,
                    Asset = entry.Asset,
                    IsPublic = entry.IsPublic
                });
                continue;
            }

            bool isVanillaMetadata = entry.Metadata.SequenceEqual(vanillaEntry.Metadata);
            bool isVanillaAsset = entry.Asset.SequenceEqual(vanillaEntry.Asset);

            if (isVanillaMetadata && isVanillaAsset) {
                continue;
            }

            changelog.Add(key, new AudioResourceAsset {
                Metadata = entry.Metadata,
                Asset = isVanillaAsset ? IsVanillaMark : entry.Asset,
                IsPublic = entry.IsPublic
            });
        }

        // Skip tracking removed assets inside
        // custom files for best compatability 
        if (!flags.IsCustomFile) {
            foreach (var (key, _) in vanilla) {
                if (src.ContainsKey(key)) {
                    continue;
                }

                changelog.Add(key, new AudioResourceAsset {
                    Metadata = FakeMetadata.ToBinary(FakeMetadataFlags.IsRemoved),
                    Asset = null
                });
            }
        }

        return changelog;
    }

    public void Dispose()
    {
    }
}