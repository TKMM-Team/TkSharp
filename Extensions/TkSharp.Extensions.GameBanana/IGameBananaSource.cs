namespace TkSharp.Extensions.GameBanana;

public enum GameBananaSortMode
{
    Generic_Newest,
    Generic_Oldest,
    Generic_LatestModified,
    Generic_NewAndUpdated,
    Generic_LatestUpdated,
    Generic_MostLiked,
    Generic_MostViewed,
    Generic_MostDownloaded
}

public interface IGameBananaSource
{
    public static GameBananaSortMode[] GameBananaSortModes => Enum.GetValues<GameBananaSortMode>();
    
    /// <summary>
    /// The 0-based page index.
    /// </summary>
    int CurrentPage { get; set; }
    
    /// <summary>
    /// The active sorting mode.
    /// </summary>
    GameBananaSortMode SortMode { get; set; }
    
    /// <summary>
    /// The active sorting mode.
    /// </summary>
    GameBananaFeed? Feed { get; }

    /// <summary>
    /// Search for a keyword and update the <see cref="Feed"/>.
    /// </summary>
    /// <param name="searchTerm">The term to search for (must be longer than 2 characters)</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask Search(string searchTerm, CancellationToken ct = default)
        => LoadPage(CurrentPage = 0, searchTerm, ct);

    /// <summary>
    /// Reload the <see cref="CurrentPage"/>.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask Reload(CancellationToken ct = default)
        => LoadPage(CurrentPage, ct: ct);

    /// <summary>
    /// Load the next page.
    /// </summary>
    /// <param name="searchTerm"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask LoadNextPage(string? searchTerm = null, CancellationToken ct = default)
        => LoadPage(++CurrentPage, null, ct);

    /// <summary>
    /// Load the specified <paramref name="page"/>.
    /// </summary>
    /// <param name="page">The 0-based page index.</param>
    /// <param name="searchTerm"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask LoadPage(int page, string? searchTerm = null, CancellationToken ct = default);
}