using TkSharp.Core.Models;

namespace TkSharp.Core;

public interface ITkSystemProvider
{
    ITkModWriter GetSystemWriter(TkModContext context, bool enableRomfsBucketing = true);
    
    ITkSystemSource GetSystemSource(string relativeFolderPath);
}