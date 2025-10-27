using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Models;

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

    [ObservableProperty]
    private object? _thumbnail;

    [JsonIgnore]
    public GameBananaMod? Full { get; private set; }

    public async ValueTask DownloadFullMod(CancellationToken ct = default)
    {
        Full = await GameBanana.GetMod(Id, ct);
    }

    public async ValueTask DownloadThumbnail(CancellationToken ct = default)
    {
        if (Media.Images.FirstOrDefault() is not { } img || TkThumbnail.CreateBitmap is null) {
            return;
        }

        try {
            await using var image = await GameBanana.Get($"{img.BaseUrl}/{img.SmallFile}", ct);
            await using MemoryStream ms = new();
            await image.CopyToAsync(ms, ct);
            ms.Seek(0, SeekOrigin.Begin);

            Thumbnail = TkThumbnail.CreateBitmap(ms);
        }
        catch (HttpRequestException ex) {
            var truncatedEx = ex.ToString().Split(Environment.NewLine)[0];
            TkLog.Instance.LogWarning("Failed to download GameBanana thumbnails: {Message}", truncatedEx);
        }
        catch (Exception ex) {
            TkLog.Instance.LogWarning("Failed to download GameBanana thumbnails: {Message}", ex);
        }

    }
}