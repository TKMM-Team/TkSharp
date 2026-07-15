using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public sealed partial class GameBananaSource(int gameId) : ObservableObject, IGameBananaSource
{
    [ObservableProperty]
    private int _currentPage;

    [ObservableProperty]
    private GameBananaSortMode _sortMode;

    [ObservableProperty]
    private GameBananaFeed? _feed;
    
    public async ValueTask LoadPage(int page, string? searchTerm = null, CancellationToken ct = default)
    {
        var sort = SortMode.ToString();
        Feed = new GameBananaFeed();
        await GameBanana.FillFeed(Feed, gameId, page, sort, searchTerm, ct);
        await GameBananaFeedFilter.FilterFullMods(Feed, ct);
    }
}