using BfresLibrary;
using TkSharp.Core;

namespace TkSharp.Merging.Bfres;

public sealed class BfresMcProcessor(ITkRom rom)
{
    private const string SHADER_STRING_CANONICAL = "Shader/ExternalBinaryString.bfres.mc";

    private static readonly Lock ProcessLock = new();
    private static bool _stringCacheLoaded;

    public static bool IsTarget(in TkPath path)
        => path.Extension is ".mc"
           || (path.Extension is ".bfres" && path.Attributes.HasFlag(TkFileAttributes.HasMcExtension));

    public byte[]? ProcessIfChanged(ArraySegment<byte> mcData)
    {
        lock (ProcessLock) {
            EnsureStringCacheLoaded();

            if (!TryOpenResFile(mcData, out var resFile)) {
                return null;
            }

            var changed = BfresCleaner.RemoveUnusedMaterials(resFile);
            changed |= BfresUpdater.Apply(resFile, rom);

            if (!changed) {
                return null;
            }

            using var ms = new MemoryStream();
            resFile.Save(ms);
            return BfresMcCodec.Compress(ms.ToArray());
        }
    }

    private void EnsureStringCacheLoaded()
    {
        if (_stringCacheLoaded) {
            return;
        }

        using var vanilla = rom.GetVanilla(SHADER_STRING_CANONICAL, out _);

        if (!vanilla.IsEmpty) {
            _ = new ResFile(new MemoryStream(BfresMcCodec.Decompress(vanilla.Segment)));
        }

        _stringCacheLoaded = true;
    }

    private static bool TryOpenResFile(ArraySegment<byte> mcData, out ResFile resFile)
    {
        try {
            resFile = new ResFile(new MemoryStream(BfresMcCodec.Decompress(mcData)));
            return true;
        }
        catch {
            resFile = null!;
            return false;
        }
    }
}
