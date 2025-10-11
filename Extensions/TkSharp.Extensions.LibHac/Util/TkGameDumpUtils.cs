using CommunityToolkit.HighPerformance.Buffers;
using TkSharp.Core.IO.Parsers;

namespace TkSharp.Extensions.LibHac.Util;

public static class TkGameDumpUtils
{
    private const string BASE_GAME_NSO_ID = "082ce09b06e33a123cb1e2770f5f9147709033db";
    
    public static bool CheckGameDump(string gameDumpFolder, out bool hasUpdate)
        => CheckGameDump(gameDumpFolder, out hasUpdate, out _);
    
    public static bool CheckGameDump(string gameDumpFolder, out bool hasUpdate, out int version)
    {
        version = 0;
        hasUpdate = false;
        
        string regionLangMaskFilePath = Path.Combine(gameDumpFolder, "System", "RegionLangMask.txt");
        if (!File.Exists(regionLangMaskFilePath)) {
            return false;
        }
        
        using var fs = File.OpenRead(regionLangMaskFilePath);
        int size = (int)fs.Length;
        
        using var buffer = SpanOwner<byte>.Allocate(size);
        fs.ReadExactly(buffer.Span);

        version = RegionLangMaskParser.ParseVersion(buffer.Span, out string nsoBinaryId);
        hasUpdate = nsoBinaryId != BASE_GAME_NSO_ID; 
        return true;
    }
}