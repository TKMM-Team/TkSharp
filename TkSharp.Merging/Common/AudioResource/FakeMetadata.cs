using Entish;

namespace TkSharp.Merging.Common.AudioResource;

[Flags]
public enum FakeMetadataFlags
{
    /// <summary>
    /// Not a valid fake metadata
    /// </summary>
    None = 0,
    
    /// <summary>
    /// This key has been removed by modification
    /// </summary>
    IsRemoved = 1
}

public unsafe struct FakeMetadata()
{
    public const uint AMTA_MAGIC = 0x41544D41;
    public const ushort AMTA_FAKE_VERSION = 0x4B54;

    public uint Magic = AMTA_MAGIC;
    public Endianness Endianness = Endianness.Little;
    public readonly ushort Version = AMTA_FAKE_VERSION;
    public int FileSize = sizeof(FakeMetadata);
    public FakeMetadataFlags Flags;

    public static FakeMetadata FromBinary(in ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(FakeMetadata)) {
            return default;
        }

        fixed (byte* ptr = data) {
            return *(FakeMetadata*)ptr;
        }
    }

    public static byte[] ToBinary(FakeMetadataFlags flags = FakeMetadataFlags.None)
    {
        var buffer = new byte[sizeof(FakeMetadata)];

        fixed (byte* ptr = buffer) {
            *(FakeMetadata*)ptr = new FakeMetadata {
                Flags = flags,
            };
        }
        
        return buffer;
    }
}