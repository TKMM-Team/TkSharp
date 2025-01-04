using LibHac.Common.Keys;
using LibHac.FsSystem;
using LibHac.Tools.FsSystem;
using TkSharp.Core;
using TkSharp.Core.Common;
using TkSharp.Data.Embedded;
using TkSharp.Extensions.LibHac;

namespace TkSharp.Debug;

public sealed class DebugRomProvider : Singleton<DebugRomProvider>, ITkRomProvider
{
    public ITkRom GetRom()
    {
        // return new ExtractedTkRom(@"F:\Games\RomFS\Totk\1.2.1",
        //     TkChecksums.FromStream(TkEmbeddedDataSource.GetChecksumsBin())
        // );

        // return new PackedTkRom(
        //     TkChecksums.FromStream(TkEmbeddedDataSource.GetChecksumsBin()),
        //     @"C:\Users\ArchLeaders\AppData\Roaming\Ryujinx\system",
        //     @"D:\Games\Emulation\Packaged\Tears-of-the-Kingdom\TotK-1.0.0.xci",
        //     @"D:\Games\Emulation\Packaged\Tears-of-the-Kingdom\TotK-1.2.1.nsp");

        return TkRomHelper.CreateRom(
            TkChecksums.FromStream(TkEmbeddedDataSource.GetChecksumsBin()),
            @"F:\switch",
            TkRomHelper.RomSource.SplitFiles, @"C:\Games\Switch games\TOTKSPLIT",
            TkRomHelper.RomSource.SdCard, @"F:\");
    }
}