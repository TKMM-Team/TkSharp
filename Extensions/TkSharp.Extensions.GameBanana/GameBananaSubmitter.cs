using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public class GameBananaSubmitter
{
    [JsonPropertyName("_sName")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_sProfileUrl")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("_sAvatarUrl")]
    public string AvatarUrl { get; set; } = string.Empty;
}
