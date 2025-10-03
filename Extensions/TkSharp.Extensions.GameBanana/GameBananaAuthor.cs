using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TkSharp.Extensions.GameBanana;

public sealed class GameBananaAuthor : INotifyPropertyChanged
{
    [JsonPropertyName("_sRole")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("_sName")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_sProfileUrl")]
    public string ProfileUrl { get; set; } = string.Empty;

    [JsonPropertyName("_sAvatarUrl")]
    public string AvatarUrl { get; set; } = string.Empty;
    
    private object? _loadedAvatar;
    public object? LoadedAvatar 
    { 
        get => _loadedAvatar;
        set {
            if (_loadedAvatar == value) {
                return;
            }
            _loadedAvatar = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}