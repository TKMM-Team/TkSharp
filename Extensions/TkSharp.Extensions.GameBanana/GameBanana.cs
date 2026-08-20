using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TkSharp.Extensions.GameBanana.Helpers;

namespace TkSharp.Extensions.GameBanana;

public static class GameBanana
{
    private const int MAX_RETRIES = 5;
    
    private const string ROOT = "https://gamebanana.com/apiv12";
    private const string PROFILE_ENDPOINT = "/{0}/{1}/ProfilePage";
    private const string FEED_ENDPOINT = "/{0}/Index?_aFilters[Generic_Game]={1}&_aFilters[Generic_ContentRatings]=-&_nPage={2}&_sSort={3}&_nPerpage=30";
    private const string FEED_ENDPOINT_SEARCH = "/{0}/Index?_aFilters[Generic_Game]={1}&_aFilters[Generic_ContentRatings]=-&_nPage={2}&_sSort={3}&_aFilters[Generic_Name]=contains,{4}&_nPerpage=30";
    private const string MEMBER_FEED_ENDPOINT = "/{0}/Index?_aFilters[Generic_Game]={1}&_aFilters[Generic_ContentRatings]=-&_aFilters[Generic_Submitter]={2}&_nPage={3}&_nPerpage=50";
    
    public static async ValueTask<Stream> Get(string url, CancellationToken ct = default)
    {
        var attempts = 0;
        
    Retry:
        try {
            attempts++;
            return await DownloadHelper.Client.GetStreamAsync(url, ct);
        }
        catch (HttpRequestException ex) {
            if (ex.StatusCode is HttpStatusCode.BadGateway && attempts < MAX_RETRIES) {
                goto Retry;
            }

            throw;
        }
    }
    
    public static async ValueTask<T?> Get<T>(string path, JsonTypeInfo<T>? typeInfo = null, CancellationToken ct = default)
    {
        var attempts = 0;

    Retry:
        try {
            attempts++;
            return await (typeInfo is not null
                ? DownloadHelper.Client.GetFromJsonAsync($"{ROOT}/{path}", typeInfo, ct)
                : DownloadHelper.Client.GetFromJsonAsync<T>($"{ROOT}/{path}", cancellationToken: ct)
            );
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadGateway && attempts < MAX_RETRIES) {
            goto Retry;
        }
        catch (JsonException) when (attempts < MAX_RETRIES) {
            goto Retry;
        }
    }

    public static async ValueTask<GameBananaSubmission?> GetSubmission(
        long id, GameBananaSubmissionType type = GameBananaSubmissionType.Mod, CancellationToken ct = default)
    {
        var submission = await Get(
            string.Format(PROFILE_ENDPOINT, type.ToApiName(), id),
            GameBananaSubmissionJsonContext.Default.GameBananaSubmission, ct
        );

        if (submission is not null) {
            submission.Type = type;
        }

        return submission;
    }

    public static async ValueTask<GameBananaFeed?> FillFeed(
        GameBananaFeed feed, int gameId, int page, string sort, string? searchTerm,
        GameBananaSubmissionType type = GameBananaSubmissionType.Mod, CancellationToken ct = default)
    {
        var response = await Get(
            GetFeedEndpoint(type, gameId, page + 1, sort, searchTerm),
            GameBananaFeedJsonContext.Default.GameBananaFeed, ct
        );

        return AppendResponse(feed, response, type);
    }

    public static async ValueTask<GameBananaFeed?> FillMemberFeed(
        GameBananaFeed feed, int memberId, int gameId, int page, CancellationToken ct = default)
    {
        var pageNumber = page + 1;

        var modTask = Get(
            string.Format(MEMBER_FEED_ENDPOINT, GameBananaSubmissionType.Mod.ToApiName(), gameId, memberId, pageNumber),
            GameBananaFeedJsonContext.Default.GameBananaFeed, ct
        ).AsTask();

        var wipTask = Get(
            string.Format(MEMBER_FEED_ENDPOINT, GameBananaSubmissionType.Wip.ToApiName(), gameId, memberId, pageNumber),
            GameBananaFeedJsonContext.Default.GameBananaFeed, ct
        ).AsTask();

        await Task.WhenAll(modTask, wipTask);

        AppendResponse(feed, modTask.Result, GameBananaSubmissionType.Mod);
        AppendResponse(feed, wipTask.Result, GameBananaSubmissionType.Wip);
        feed.Metadata = MergeMetadata(modTask.Result?.Metadata, wipTask.Result?.Metadata);

        return feed;
    }

    private static GameBananaFeed AppendResponse(
        GameBananaFeed feed, GameBananaFeed? response, GameBananaSubmissionType type)
    {
        if (response is null) {
            return feed;
        }

        feed.Metadata = response.Metadata;
        foreach (var record in response.Records) {
            record.Type = type;
            feed.Records.Add(record);
        }

        return feed;
    }

    private static GameBananaMetadata MergeMetadata(GameBananaMetadata? left, GameBananaMetadata? right)
    {
        if (left is null) {
            return right ?? new GameBananaMetadata();
        }

        if (right is null) {
            return left;
        }

        return new GameBananaMetadata {
            PerPage = Math.Max(left.PerPage, right.PerPage),
            RecordCount = Math.Max(left.RecordCount, right.RecordCount),
            IsCompleted = left.IsCompleted && right.IsCompleted
        };
    }

    private static string GetFeedEndpoint(
        GameBananaSubmissionType type, int gameId, int page, string sort, string? searchTerm)
    {
        var apiName = type.ToApiName();
        return searchTerm switch {
            { Length: > 2 } => string.Format(FEED_ENDPOINT_SEARCH, apiName, gameId, page, sort, searchTerm),
            _ => string.Format(FEED_ENDPOINT, apiName, gameId, page, sort)
        };
    }
}
