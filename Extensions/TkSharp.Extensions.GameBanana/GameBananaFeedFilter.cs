namespace TkSharp.Extensions.GameBanana;

internal static class GameBananaFeedFilter
{
    public static void FilterSubmissions(GameBananaFeed feed)
    {
        for (var i = feed.Records.Count - 1; i >= 0; i--) {
            var record = feed.Records[i];
            if (record.IsObsolete || record.IsContentRated) {
                feed.Records.RemoveAt(i);
            }
        }
    }
}
