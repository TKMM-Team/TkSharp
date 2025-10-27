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

    public static RentedBuffers<byte> Allocate(ReadOnlySpan<Stream> streams, bool disposeStreams = false)
    {
        var totalBufferSize = 0;
        var sections = ArrayPool<Range>.Shared.Rent(streams.Length);
        for (var i = 0; i < streams.Length; i++) {
            var size = Convert.ToInt32(streams[i].Length);
            sections[i] = totalBufferSize..(totalBufferSize += size);
        }
        
        RentedBuffers<byte> buffers = new(totalBufferSize, sections, streams.Length);
        for (var i = 0; i < streams.Length; i++) {
            var stream = streams[i];
            _ = stream.Read(buffers[i].Span);
            
            if (disposeStreams) {
                stream.Dispose();
            }
        }

        return buffers;
    }
    
    public int Count { get; }

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
        var totalBufferSize = 0;
        
        Count = sizes.Length;
        _sections = ArrayPool<Range>.Shared.Rent(sizes.Length);
        for (var i = 0; i < sizes.Length; i++) {
            var size = sizes[i];
            _sections[i] = totalBufferSize..(totalBufferSize += size);
        }
        
        _buffer = ArrayPool<T>.Shared.Rent(totalBufferSize);
    }
    
    private RentedBuffers(int totalBufferSize, Range[] sections, int count)
    {
        _buffer = ArrayPool<T>.Shared.Rent(totalBufferSize);
        _sections = sections;
        Count = count;
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
                (var offset, var length) = _range.GetOffsetAndLength(_buffer.Length);
                return new ArraySegment<T>(_buffer, offset, length);
            }
        }
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public ref struct Enumerator(RentedBuffers<T> buffers)
    {
        private readonly RentedBuffers<T> _buffers = buffers;
        private int _current = -1;

        public bool MoveNext()
        {
            return ++_current < _buffers.Count;
        }

        public void Reset()
        {
            _current = -1;
        }

        public readonly Entry Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffers[_current];
        }

        public void Dispose()
        {
        }
    } 
}