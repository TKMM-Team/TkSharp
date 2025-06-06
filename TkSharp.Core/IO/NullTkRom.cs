using TkSharp.Core.IO.Buffers;

namespace TkSharp.Core.IO;

/// <summary>
/// A null implementation of ITkRom that acts as if no ROM data is available.
/// All operations return empty or default values.
/// </summary>
public class NullTkRom : ITkRom
{
    public int GameVersion => 121;

    public string NsoBinaryId => string.Empty;

    public TkZstd Zstd { get; } = new(new MemoryStream());

    public IDictionary<string, string> AddressTable => new Dictionary<string, string>();

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EventFlowVersions => default;

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> EffectVersions => default;

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> AiVersions => default;

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> LogicVersions => default;

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> SequenceVersions => default;

    public string CanonicalToRelativePath(string canonical, TkFileAttributes attributes) => canonical;

    public RentedBuffer<byte> GetVanilla(string canonical, TkFileAttributes attributes) => new();

    public RentedBuffer<byte> GetVanilla(string relativeFilePath) => new();

    public bool IsVanilla(ReadOnlySpan<char> canonical, Span<byte> src, int fileVersion) => false;

    public void Dispose()
    {
        // Nothing to dispose
    }
} 