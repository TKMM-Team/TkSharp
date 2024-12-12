using System.Buffers;

namespace TkSharp.Core.IO.Buffers;

public readonly struct RentedBitArray : IDisposable
{
    private readonly int[] _rented;
    private readonly int _length;

    public static RentedBitArray Create(int size)
    {
        int arrayLength = (int)Math.Ceiling(size / 32d);
        int[] rented = ArrayPool<int>.Shared.Rent(arrayLength);
        return new RentedBitArray(rented, size);
    }

    private RentedBitArray(int[] rented, int length)
    {
        _rented = rented;
        _length = length;
    }

    public bool this[int index] {
        get => IsSet(index);
        set => Set(index, value);
    }

    public void Set(int index, bool value)
    {
        if ((uint)index >= (uint)_length) {
            throw new IndexOutOfRangeException();
        }
        
        int bitMask = 1 << index;
        ref int segment = ref _rented[index >> 5];

        if (value) {
            segment |= bitMask;
        }
        else {
            segment &= ~bitMask;
        }
    }

    public bool IsSet(int index)
    {
        if ((uint)index >= (uint)_length) {
            throw new IndexOutOfRangeException();
        }
        
        return (_rented[index >> 5] & (1 << index)) != 0;
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_rented);
    }
}