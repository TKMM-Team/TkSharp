using Syroot.NintenTools.NSW.Bntx;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;

namespace TkSharp.Merging.Mergers;

public sealed class BntxMerger(ITkRom rom) : ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Dictionary<string, Texture> lastTextures = new(StringComparer.Ordinal);

        for (var i = 0; i < inputs.Count; i++) {
            var input = inputs[i].Segment;
            using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
            var bntx = new BntxFile(ms, leaveOpen: false);

            foreach (var tex in bntx.Textures) {
                lastTextures[tex.Name] = tex;
            }
        }

        using MemoryStream vanillaMs = new(vanillaData.Array!, vanillaData.Offset, vanillaData.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        ApplyTextures(entry.Canonical, vanillaBntx, lastTextures);

        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        Dictionary<string, Texture> lastTextures = new(StringComparer.Ordinal);

        foreach (var input in inputs) {
            using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
            var bntx = new BntxFile(ms, leaveOpen: false);

            foreach (var tex in bntx.Textures) {
                lastTextures[tex.Name] = tex;
            }
        }

        using MemoryStream vanillaMs = new(vanillaData.Array!, vanillaData.Offset, vanillaData.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        ApplyTextures(entry.Canonical, vanillaBntx, lastTextures);

        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        using MemoryStream vanillaMs = new(@base.Array!, @base.Offset, @base.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
        var changelogBntx = new BntxFile(ms, leaveOpen: false);

        Dictionary<string, Texture> lastTextures = new(StringComparer.Ordinal);
        foreach (var tex in changelogBntx.Textures) {
            lastTextures[tex.Name] = tex;
        }

        ApplyTextures(entry.Canonical, vanillaBntx, lastTextures);
        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    private void ApplyTextures(string bntxCanonical, BntxFile vanillaBntx, IReadOnlyDictionary<string, Texture> texturesToApply)
    {
        HashSet<string> vanillaNames = new(StringComparer.Ordinal);

        for (var i = 0; i < vanillaBntx.Textures.Count; i++) {
            var vanillaTex = vanillaBntx.Textures[i];
            vanillaNames.Add(vanillaTex.Name);

            if (!texturesToApply.TryGetValue(vanillaTex.Name, out var modTexture)) {
                continue;
            }

            var modFlat = FlattenTexture(modTexture);
            var textureCanonical = $"{bntxCanonical}/{modTexture.Name}";
            if (rom.IsVanillaAnyVersion(textureCanonical, modFlat.AsSpan())) {
                continue;
            }

            vanillaBntx.Textures[i] = modTexture;
        }

        foreach (var (name, texture) in texturesToApply) {
            if (vanillaNames.Contains(name)) {
                continue;
            }

            vanillaBntx.Textures.Add(texture);
        }
    }

    private static byte[] FlattenTexture(Texture texture)
    {
        int totalSize = 0;

        if (texture.TextureData is { Count: > 0 } textureData) {
            totalSize += textureData.SelectMany(arraySlice => arraySlice).Sum(mip => mip.Length);
        }

        byte[] flat = new byte[totalSize];
        int offset = 0;

        if (texture.TextureData is { Count: > 0 } textureData2) {
            foreach (var mip in textureData2.SelectMany(arraySlice => arraySlice)) {
                mip.CopyTo(flat.AsSpan(offset));
                offset += mip.Length;
            }
        }

        return flat;
    }
}
