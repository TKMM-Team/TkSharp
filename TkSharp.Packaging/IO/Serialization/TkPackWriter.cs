using CommunityToolkit.HighPerformance;
using TkSharp.Core.IO.Serialization;
using TkSharp.Core.Models;

namespace TkSharp.Packaging.IO.Serialization;

public static class TkPackWriter
{
    public static void Write(in Stream output, TkMod mod)
    {
        output.Write(TkBinaryWriter.TKPK_MAGIC);
        output.Write(TkBinaryWriter.TKPK_VERSION);
        
        TkBinaryWriter.WriteTkMod(output, mod);
    }
}
