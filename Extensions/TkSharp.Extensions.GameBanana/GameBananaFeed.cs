using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public class GameBananaFeed
{
    [JsonPropertyName("_aMetadata")]
    public GameBananaMetadata Metadata { get; set; } = new();

    [JsonPropertyName("_aRecords")]
    public ObservableCollection<GameBananaSubmissionRecord> Records { get; set; } = [];
}

[JsonSerializable(typeof(GameBananaFeed))]
public partial class GameBananaFeedJsonContext : JsonSerializerContext;
