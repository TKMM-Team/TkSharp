using System.Buffers;
using System.Runtime.CompilerServices;

namespace TkSharp.Core.IO.Buffers;

public readonly ref struct RentedBuffers<T> : IDisposable where T : unmanaged
{
    private readonly T[] _buffer;
    private readonly Range[] _sections;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedBuffers<T> Allocate(int[] sizes)
    {
        return new RentedBuffers<T>(sizes);
    }

    public static RentedBuffers<byte> Allocate(Stream[] streams, bool disposeStreams = false)
    {
        int totalBufferSize = 0;
        Range[] sections = ArrayPool<Range>.Shared.Rent(streams.Length);
        for (int i = 0; i < streams.Length; i++) {
            int size = Convert.ToInt32(streams[i].Length);
            sections[i] = totalBufferSize..(totalBufferSize += size);
        }
        
        RentedBuffers<byte> buffers = new(totalBufferSize, sections);
        for (int i = 0; i < streams.Length; i++) {
            Stream stream = streams[i];
            _ = stream.Read(buffers[i].Span);
            
            if (disposeStreams) {
                stream.Dispose();
            }
        }

        return buffers;
    }

    public Entry this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_buffer, _sections[index]);
    }
    
    public RentedBuffers()
    {
        _buffer = [];
        _sections = [];
    }
    
    private RentedBuffers(ReadOnlySpan<int> sizes)
    {
        int totalBufferSize = 0;
        
        _sections = ArrayPool<Range>.Shared.Rent(sizes.Length);
        for (int i = 0; i < sizes.Length; i++) {
            int size = sizes[i];
            _sections[i] = totalBufferSize..(totalBufferSize += size);
        }
        
        _buffer = ArrayPool<T>.Shared.Rent(totalBufferSize);
    }
    
    private RentedBuffers(int totalBufferSize, Range[] sections)
    {
        _sections = sections;
        _buffer = ArrayPool<T>.Shared.Rent(totalBufferSize);
    }
    
    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_buffer);
        ArrayPool<Range>.Shared.Return(_sections);
    }

    public readonly ref struct Entry(T[] buffer, Range range)
    {
        private readonly T[] _buffer = buffer;
        private readonly Range _range = range;
        
        public Span<T> Span {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.AsSpan(_range);
        }

        public Memory<T> Memory {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.AsMemory(_range);
        }

        public ArraySegment<T> Segment {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                (int offset, int length) = _range.GetOffsetAndLength(_buffer.Length);
                return new ArraySegment<T>(_buffer, offset, length);
            }
        }
    }
}