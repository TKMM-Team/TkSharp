namespace TkSharp.Extensions.GameBanana;

internal static class GameBananaFeedFilter
{
    public static bool IsMemberMod(GameBananaModRecord record, int gameId)
        => record is { IsContentRated: false } && record.Game.Id == gameId;

    public static async ValueTask FilterFullMods(GameBananaFeed feed, CancellationToken ct)
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
