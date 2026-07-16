using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public class GameBananaMetadata
{
    [JsonPropertyName("_nRecordCount")]
    public int RecordCount { get; set; }

    [JsonPropertyName("_bIsComplete")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("_nPerpage")]
    public int PerPage { get; set; }

    /// <summary>
    /// Total number of pages for this feed. One page when there are at most <see cref="PerPage"/> records.
    /// </summary>
    [JsonIgnore]
    public int TotalPageCount => PerPage > 0 && RecordCount > PerPage
        ? (int)Math.Ceiling(RecordCount / (double)PerPage)
        : 1;
}
