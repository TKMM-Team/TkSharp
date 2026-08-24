using System.Buffers.Binary;
using System.Text;

namespace TkSharp.Merging.Mergers.Asb;

internal sealed class BinaryDataReader(ReadOnlyMemory<byte> data)
{
    private readonly ReadOnlyMemory<byte> _data = data;

    public int Position { get; set; }
    public int Length => _data.Length;

    public byte ReadByte()
    {
        Ensure(1);
        return _data.Span[Position++];
    }

    public ushort ReadUInt16()
    {
        Ensure(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Span[Position..]);
        Position += 2;
        return value;
    }

    public short ReadInt16() => unchecked((short)ReadUInt16());

    public uint ReadUInt32()
    {
        Ensure(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span[Position..]);
        Position += 4;
        return value;
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public ulong ReadUInt64()
    {
        Ensure(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Span[Position..]);
        Position += 8;
        return value;
    }

    public float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadUInt32());

    public byte[] ReadBytes(int count)
    {
        Ensure(count);
        var value = _data.Slice(Position, count).ToArray();
        Position += count;
        return value;
    }

    public string ReadFixedString(int count)
    {
        var bytes = ReadBytes(count);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    public string ReadCStringAt(long offset)
    {
        if (offset < 0 || offset >= _data.Length) {
            throw new InvalidDataException($"String offset 0x{offset:x} exceeds file size 0x{_data.Length:x}.");
        }

        var span = _data.Span[(int)offset..];
        var end = span.IndexOf((byte)0);
        if (end < 0) {
            throw new InvalidDataException($"String at 0x{offset:x} is not null terminated.");
        }

        return Encoding.UTF8.GetString(span[..end]);
    }

    public void Seek(long position)
    {
        if (position < 0 || position > _data.Length) {
            throw new InvalidDataException($"Offset 0x{position:x} exceeds file size 0x{_data.Length:x}.");
        }

        Position = checked((int)position);
    }

    public void Skip(int count) => Seek(checked(Position + count));

    private void Ensure(int count)
    {
        if (count < 0 || Position > _data.Length - count) {
            throw new EndOfStreamException();
        }
    }
}

internal sealed class BinaryDataWriter
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;

    public BinaryDataWriter()
    {
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    public int Position {
        get => checked((int)_stream.Position);
        set {
            if (value < 0) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value > _stream.Length) {
                _stream.SetLength(value);
            }

            _stream.Position = value;
        }
    }

    public void Write(byte value) => _writer.Write(value);
    public void Write(ushort value) => _writer.Write(value);
    public void Write(short value) => _writer.Write(value);
    public void Write(uint value) => _writer.Write(value);
    public void Write(int value) => _writer.Write(value);
    public void Write(ulong value) => _writer.Write(value);
    public void Write(float value) => _writer.Write(value);
    public void Write(ReadOnlySpan<byte> value) => _writer.Write(value);

    public void WriteFixedString(string value, int size)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > size) {
            throw new InvalidDataException($"Fixed string exceeds {size} bytes.");
        }

        Write(bytes);
        Write(new byte[size - bytes.Length]);
    }

    public int ReserveUInt32()
    {
        var position = Position;
        Write(0u);
        return position;
    }

    public int ReserveUInt64()
    {
        var position = Position;
        Write(0ul);
        return position;
    }

    public void PatchUInt32(int position, uint value)
    {
        var current = Position;
        Position = position;
        Write(value);
        Position = current;
    }

    public void PatchUInt64(int position, ulong value)
    {
        var current = Position;
        Position = position;
        Write(value);
        Position = current;
    }

    public void Align(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        var aligned = (Position + alignment - 1) & -alignment;
        if (aligned != Position) {
            Write(new byte[aligned - Position]);
        }
    }

    public byte[] ToArray() => _stream.ToArray();
}
