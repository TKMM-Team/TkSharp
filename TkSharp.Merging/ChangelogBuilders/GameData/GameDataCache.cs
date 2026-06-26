using BsDiff;
using TkSharp.Core;

namespace TkSharp.Merging.ChangelogBuilders.GameData;

public static class GameDataCache
{
    private static readonly string _cacheFolderPath = Path.Combine(AppContext.BaseDirectory, ".gdcache");
    private static readonly int[] _gameDataVersions = [110, 140];

    /// <summary>
    /// Generate all GDL file versions from precompiled deltas
    /// </summary>
    /// <param name="tk"></param>
    public static void Cache(ITkRom tk)
    {
        Directory.CreateDirectory(_cacheFolderPath);

        if (_gameDataVersions.All(version => File.Exists(GetGameDataCacheFilePath(version)))) {
            return;
        }

        using var vanilla = tk.GetVanilla("GameData/GameDataList.Product.byml", TkFileAttributes.HasZsExtension | TkFileAttributes.HasZsExtension);
        using var ms = new MemoryStream(vanilla.Segment.Array ?? [], 0, vanilla.Segment.Count);

        int gameVersion = GetRomGameDataVersion(tk.GameVersion);
        File.WriteAllBytes(GetGameDataCacheFilePath(gameVersion), vanilla.Span);

        int startIndex = _gameDataVersions.IndexOf(gameVersion);

        // Create all higher GDL versions
        for (int i = startIndex + 1; i < _gameDataVersions.Length; i++) {
            var targetVersion = _gameDataVersions[i];
            using var fs = File.Create(GetGameDataCacheFilePath(targetVersion));
            MakeGameDataFile(_gameDataVersions[i - 1], targetVersion, fs);
        }

        // Create all lower GDL versions
        for (int i = startIndex - 1; i >= 0; i--) {
            var targetVersion = _gameDataVersions[i];
            using var fs = File.Create(GetGameDataCacheFilePath(targetVersion));
            MakeGameDataFile(_gameDataVersions[i + 1], targetVersion, fs);
        }
    }

    public static byte[] GetCachedFor(int fileVersion)
        => File.ReadAllBytes(GetGameDataCacheFilePath(fileVersion));

    private static void MakeGameDataFile(int sourceVersion, int targetVersion, Stream output)
        => BinaryPatch.Apply(GetGameDataFile(sourceVersion), () => GetGameDataDelta(sourceVersion, targetVersion), output);

    private static FileStream GetGameDataFile(int version)
        => File.OpenRead(GetGameDataCacheFilePath(version));

    private static Stream GetGameDataDelta(int sourceVersion, int targetVersion)
        => typeof(GameDataCache).Assembly.GetManifestResourceStream(
            $"TkSharp.Merging.Resources.GameDataDelta.GameDataDelta.{sourceVersion}-{targetVersion}.gdldelta")!;

    private static string GetGameDataCacheFilePath(int version)
        => Path.Combine(_cacheFolderPath, $"{version}.gdcache");

    public static int GetRomGameDataVersion(int gameVersion) => _gameDataVersions.Last(ver => ver <= gameVersion);
}