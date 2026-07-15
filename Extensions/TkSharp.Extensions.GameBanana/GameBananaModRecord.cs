using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public partial class GameBananaModRecord : ObservableObject
{
    [JsonPropertyName("_idRow")]
    public int Id { get; set; }

    [JsonPropertyName("_sName")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_bHasContentRatings")]
    public bool IsContentRated { get; set; }

    [JsonPropertyName("_bIsObsolete")]
    public bool IsObsolete { get; set; }

    [JsonPropertyName("_sProfileUrl")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("_aPreviewMedia")]
    public GameBananaMedia Media { get; set; } = new();

    [JsonPropertyName("_aSubmitter")]
    public GameBananaSubmitter Submitter { get; set; } = new();

    [JsonPropertyName("_sVersion")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("_sModelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("_aGame")]
    public GameBananaGame Game { get; set; } = new();

    [ObservableProperty]
    private object? _thumbnail;

    [JsonIgnore]
    public GameBananaMod? Full { get; private set; }

    [JsonIgnore]
    public string? ThumbnailUrl => Media.Images.FirstOrDefault() is { } img
        ? $"{img.BaseUrl}/{img.SmallFile}"
        : null;

    public async ValueTask DownloadFullMod(CancellationToken ct = default)
    {
        Full = await GameBanana.GetMod(Id, ct);
    }
}