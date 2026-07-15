using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public sealed partial class GameBananaMemberSource(int memberId, int gameId) : ObservableObject, IGameBananaSource
{
    public int MemberId => memberId;

    [ObservableProperty]
    private int _currentPage;

    [ObservableProperty]
    private GameBananaSortMode _sortMode;

    [ObservableProperty]
    private GameBananaFeed? _feed;

    public async ValueTask LoadPage(int page, string? searchTerm = null, CancellationToken ct = default)
    {
        Feed = new GameBananaFeed();
        await GameBanana.FillMemberFeed(Feed, memberId, gameId, page, ct);
        await GameBananaFeedFilter.FilterFullMods(Feed, ct);
    }
}
