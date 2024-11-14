using System.Buffers;
using System.Diagnostics;
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
        int read = stream.Read(result._buffer, 0, size);
        Debug.Assert(read == size);
        return result;
    }

    public Span<T> Span {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.AsSpan(.._size);
    }

    public Memory<T> Memory {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.AsMemory(.._size);
    }

    public ArraySegment<T> Segment {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

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

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_buffer);
    }
}