using System.Buffers;
using System.Runtime.CompilerServices;

namespace TkSharp.Core.IO.Buffers;

public ref struct RentedBuffer<T> : IDisposable where T : unmanaged
{
    private readonly T[] _buffer;
    private int _size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedBuffer<T> Allocate(int size)
    {
        return new RentedBuffer<T>(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedBuffer<byte> Allocate(Stream stream)
    {
        int size = (int)stream.Length;
        RentedBuffer<byte> result = RentedBuffer<byte>.Allocate(size);
        stream.ReadExactly(result._buffer, 0, size);
        return result;
    }

    public Span<T> Span {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Segment.AsSpan();
    }

    public Memory<T> Memory {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Segment.AsMemory();
    }

    public ArraySegment<T> Segment {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

    public bool IsEmpty => _size == 0;

    public RentedBuffer()
    {
        _buffer = [];
        Segment = [];
    }
    
    private RentedBuffer(int size)
    {
        _buffer = ArrayPool<T>.Shared.Rent(size);
        _size = size;
        Segment = new ArraySegment<T>(_buffer, 0, _size);
    }

    public void Resize(int size)
    {
        _size = size;
        Segment = new ArraySegment<T>(_buffer, 0, size);
    }

    public void Slice(int startOffset, int endOffset)
    {
        _size = endOffset - startOffset;
        Segment = new ArraySegment<T>(_buffer, startOffset, _size);
    }

    public readonly void Dispose()
    {
        if (_buffer is not null) {
            ArrayPool<T>.Shared.Return(_buffer);
        }
    }
}