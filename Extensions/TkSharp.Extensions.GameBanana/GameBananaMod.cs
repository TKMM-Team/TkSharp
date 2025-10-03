using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public sealed class GameBananaMod
{
    [JsonPropertyName("_idRow")]
    public long Id { get; set; }

    [JsonPropertyName("_sName")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_aPreviewMedia")]
    public GameBananaMedia Media { get; set; } = new();

    [JsonPropertyName("_sVersion")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("_bIsPrivate")]
    public bool IsPrivate { get; set; }

    [JsonPropertyName("_bIsFlagged")]
    public bool IsFlagged { get; set; }

    [JsonPropertyName("_bIsTrashed")]
    public bool IsTrashed { get; set; }

    [JsonPropertyName("_aFiles")]
    public List<GameBananaFile> Files { get; set; } = [];

    [JsonPropertyName("_sText")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("_sDescription")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("_aSubmitter")]
    public GameBananaSubmitter Submitter { get; set; } = new();

    [JsonPropertyName("_aGame")]
    public GameBananaGame Game { get; set; } = new();

    [JsonPropertyName("_aCredits")]
    public List<GameBananaCreditGroup> Credits { get; set; } = [];

    [JsonPropertyName("_nLikeCount")]
    public int LikeCount { get; set; }

    [JsonPropertyName("_nViewCount")]
    public int ViewCount { get; set; }

    [JsonPropertyName("_nDownloadCount")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("_tsDateAdded")]
    public long DateAdded { get; set; }

    [JsonPropertyName("_tsDateUpdated")]
    public long DateUpdated { get; set; }

    [JsonPropertyName("_tsDateModified")]
    public long DateModified { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

[JsonSerializable(typeof(GameBananaMod))]
internal partial class GameBananaModJsonContext : JsonSerializerContext;
