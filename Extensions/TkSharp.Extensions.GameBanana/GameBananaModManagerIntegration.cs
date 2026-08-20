using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public sealed class GameBananaModManagerIntegration
{
    [JsonPropertyName("_sModManagerAlias")]
    public string ModManagerAlias { get; set; } = string.Empty;
}
