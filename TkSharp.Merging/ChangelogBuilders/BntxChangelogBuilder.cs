using Microsoft.Extensions.Logging;
using Syroot.NintenTools.NSW.Bntx;
using TkSharp.Core;

namespace TkSharp.Merging.ChangelogBuilders;

public sealed class BntxChangelogBuilder(ITkRom tk, bool disposeTkRom) : ITkChangelogBuilder
{
    public bool CanProcessWithoutVanilla => true;

    public bool Build(string canonical, in TkPath path, in TkChangelogBuilderFlags flags, ArraySegment<byte> srcBuffer,
        ArraySegment<byte> vanillaBuffer, OpenWriteChangelog openWrite, int gameVersion)
    {
        using MemoryStream srcStream = new(srcBuffer.Array!, srcBuffer.Offset, srcBuffer.Count, writable: false, publiclyVisible: true);
        var bntx = new BntxFile(srcStream, leaveOpen: false);

        List<Texture> nonVanillaTextures = [];

        foreach (var texture in bntx.Textures) {
            byte[] flat = FlattenTexture(texture);
            var textureCanonical = $"{canonical}/{texture.Name}";

            if (tk.IsVanillaAnyVersion(textureCanonical, flat.AsSpan())) {
                continue;
            }

            TkLog.Instance.LogDebug("Found modded texture '{Texture}' in '{Bntx}'", texture.Name, canonical);
            nonVanillaTextures.Add(texture);
        }

        if (nonVanillaTextures.Count == 0) {
            return false;
        }

        bntx.Textures = nonVanillaTextures;

        using MemoryStream ms = new();
        bntx.Save(ms, leaveOpen: true);
        ms.Seek(0, SeekOrigin.Begin);

        using var output = openWrite(path, canonical);
        ms.CopyTo(output);
        return true;
    }

    private static byte[] FlattenTexture(Texture texture)
    {
        int totalSize = 0;

        if (texture.TextureData is { Count: > 0 } textureData) {
            foreach (var arraySlice in textureData) {
                foreach (var mip in arraySlice) {
                    totalSize += mip.Length;
                }
            }
        }

        byte[] flat = new byte[totalSize];
        int offset = 0;

        if (texture.TextureData is { Count: > 0 } textureData2) {
            foreach (var arraySlice in textureData2) {
                foreach (var mip in arraySlice) {
                    mip.CopyTo(flat.AsSpan(offset));
                    offset += mip.Length;
                }
            }
        }

        return flat;
    }

    public void Dispose()
    {
        if (disposeTkRom) {
            tk.Dispose();
        }
    }
}