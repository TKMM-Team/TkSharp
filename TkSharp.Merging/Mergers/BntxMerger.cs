using Syroot.NintenTools.NSW.Bntx;
using TkSharp.Core;
using TkSharp.Core.IO.Buffers;
using TkSharp.Core.Models;

namespace TkSharp.Merging.Mergers;

public sealed class BntxMerger(ITkRom rom) : ITkMerger
{
    public MergeResult Merge(TkChangelogEntry entry, RentedBuffers<byte> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        using MemoryStream vanillaMs = new(vanillaData.Array!, vanillaData.Offset, vanillaData.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        for (var i = 0; i < inputs.Count; i++) {
            var input = inputs[i].Segment;
            using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
            var changelogBntx = new BntxFile(ms, leaveOpen: false);
            ApplyTextures(entry.Canonical, vanillaBntx, changelogBntx.Textures);
        }

        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    public MergeResult Merge(TkChangelogEntry entry, IEnumerable<ArraySegment<byte>> inputs, ArraySegment<byte> vanillaData, Stream output)
    {
        using MemoryStream vanillaMs = new(vanillaData.Array!, vanillaData.Offset, vanillaData.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        foreach (var input in inputs) {
            using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
            var changelogBntx = new BntxFile(ms, leaveOpen: false);
            ApplyTextures(entry.Canonical, vanillaBntx, changelogBntx.Textures);
        }

        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    public MergeResult MergeSingle(TkChangelogEntry entry, ArraySegment<byte> input, ArraySegment<byte> @base, Stream output)
    {
        using MemoryStream vanillaMs = new(@base.Array!, @base.Offset, @base.Count, writable: false, publiclyVisible: true);
        var vanillaBntx = new BntxFile(vanillaMs, leaveOpen: false);

        using MemoryStream ms = new(input.Array!, input.Offset, input.Count, writable: false, publiclyVisible: true);
        var changelogBntx = new BntxFile(ms, leaveOpen: false);

        ApplyTextures(entry.Canonical, vanillaBntx, changelogBntx.Textures);
        vanillaBntx.Save(output, leaveOpen: true);
        return MergeResult.Default;
    }

    private void ApplyTextures(string bntxCanonical, BntxFile targetBntx, IList<Texture> texturesToApply)
    {
        Dictionary<string, int> indexByName = new(StringComparer.Ordinal);
        for (var i = 0; i < targetBntx.Textures.Count; i++) {
            indexByName[targetBntx.Textures[i].Name] = i;
        }

        foreach (var texture in texturesToApply) {
            var flat = FlattenTexture(texture);
            var textureCanonical = $"{bntxCanonical}/{texture.Name}";
            if (rom.IsVanillaAnyVersion(textureCanonical, flat.AsSpan())) {
                continue;
            }

            if (indexByName.TryGetValue(texture.Name, out var index)) {
                targetBntx.Textures[index] = texture;
                continue;
            }

            indexByName[texture.Name] = targetBntx.Textures.Count;
            targetBntx.Textures.Add(texture);
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
