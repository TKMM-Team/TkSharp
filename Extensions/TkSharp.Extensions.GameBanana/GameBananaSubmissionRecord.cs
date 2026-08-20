using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public partial class GameBananaSubmissionRecord : ObservableObject
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

    [JsonPropertyName("type")]
    public GameBananaSubmissionType Type { get; set; } = GameBananaSubmissionType.Mod;

    [ObservableProperty]
    private object? _thumbnail;

    [JsonIgnore]
    public GameBananaSubmission? Full { get; private set; }

    [JsonIgnore]
    public string? ThumbnailUrl => Media.Images.FirstOrDefault() is { } img
        ? $"{img.BaseUrl}/{img.SmallFile}"
        : null;

    public async ValueTask DownloadFullSubmission(CancellationToken ct = default)
    {
        Full = await GameBanana.GetSubmission(Id, Type, ct);
    }
}