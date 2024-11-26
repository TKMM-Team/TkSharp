﻿using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TkSharp.Extensions.GameBanana;

public partial class GameBananaFile : ObservableObject
{
    [JsonPropertyName("_idRow")]
    public long Id { get; set; }

    [JsonPropertyName("_sFile")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_sDescription")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("_sDownloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("_sMd5Checksum")]
    public string Checksum { get; set; } = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSelected;

    [JsonIgnore]
    public bool IsTkcl => Name.EndsWith(".tkcl");
}