using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public sealed partial class GameBananaSource(int gameId) : ObservableObject, IGameBananaSource
{
    private readonly int _gameId = gameId;

    [ObservableProperty]
    private int _currentPage;

    [ObservableProperty]
    private GameBananaSortMode _sortMode;

    [ObservableProperty]
    private GameBananaFeed? _feed;
    
    public async ValueTask LoadPage(int page, string? searchTerm = null, CancellationToken ct = default)
    {
        var sort = SortMode.ToString().ToLower();
        Feed = new GameBananaFeed();
        await GameBanana.FillFeed(Feed, _gameId, page, sort, searchTerm, ct);
        await FilterRecords(Feed, ct);
    }

    private static async ValueTask FilterRecords(GameBananaFeed feed, CancellationToken ct)
    {
        for (var i = 0; i < feed.Records.Count; i++) {
            var record = feed.Records[i];
            await record.DownloadFullMod(ct);
            
            if (ct.IsCancellationRequested) {
                break;
            }

            var isRecordClean = record is {
                Full: {
                    IsTrashed: false, IsFlagged: false, IsPrivate: false
                },
                IsObsolete: false, IsContentRated: false
            };

            if (isRecordClean) {
                continue;
            }
            
            feed.Records.RemoveAt(i);
            i--;
        }
    }
}