using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp;

public static class TkSystemUpdater
{
    public static bool UpdateToData2(string dataFolder, string legacyDataFolder)
    {
        var legacy = TkModManager.CreateLegacy(dataFolder, legacyDataFolder, TkSystemVersion.V1);

        // Upgrade
        
        foreach (TkMod mod in legacy.Mods) {
            
        }

        return false;
    }
}