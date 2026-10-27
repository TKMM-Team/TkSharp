using BymlLibrary;
using Microsoft.Extensions.Logging;
using Revrs;
using TkSharp.Core;

namespace TkSharp.Merging;

/// <summary>
/// Raises <c>Heaps.AI.Size</c> when the file count under <c>AI/</c> inside
/// the merged <c>Pack/AI.Global.Product.pack</c> exceeds the current limit.
/// See https://github.com/TKMM-Team/Tkmm/issues/177
/// </summary>
internal static class TkAiHeapDefPatcher
{
    private const string HEAP_DEF_CANONICAL = "System/HeapSize/Product.Nin_NX_NVN.HeapDef.byml";
    private const TkFileAttributes HEAP_DEF_ATTRIBUTES = TkFileAttributes.HasZsExtension | TkFileAttributes.IsProductFile;

    public static bool IsHeapDefRelativePath(ITkRom rom, string relativePath)
        => relativePath == rom.CanonicalToRelativePath(HEAP_DEF_CANONICAL, HEAP_DEF_ATTRIBUTES);

    public static void Apply(TkMerger merger, ITkRom rom, int aiFileCount)
    {
        if (aiFileCount <= 0) {
            return;
        }

        var relativePath = rom.CanonicalToRelativePath(HEAP_DEF_CANONICAL, HEAP_DEF_ATTRIBUTES);

        Byml heapDef;
        Endianness endianness;
        ushort version;

        if (merger.MergedHeapDef is { } merged) {
            heapDef = Byml.FromBinary(merged, out endianness, out version);
        }
        else {
            using var vanilla = rom.GetVanilla(relativePath);

            if (vanilla.IsEmpty) {
                return;
            }

            heapDef = Byml.FromBinary(vanilla.Span, out endianness, out version);
        }

        var ai = heapDef.GetMap()["Heaps"].GetMap()["AI"].GetMap();
        var currentSize = (int)ai["Size"].Value!;

        if (aiFileCount <= currentSize) {
            return;
        }

        ai["Size"] = aiFileCount;

        using MemoryStream ms = new();
        heapDef.WriteBinary(ms, endianness, version);
        merger.CopyMergedToSimpleOutput(ms, relativePath, HEAP_DEF_ATTRIBUTES, zsDictionaryId: 1);

        TkLog.Instance.LogInformation(
            "Adjusted AI HeapDef Size from {Previous} to {Size}",
            currentSize, aiFileCount);
    }
}
