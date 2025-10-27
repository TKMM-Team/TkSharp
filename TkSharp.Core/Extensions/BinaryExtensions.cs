using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

namespace TkSharp.Core.Extensions;

public static class BinaryExtensions
{
    public static string? ReadString(this Stream input)
    {
        var length = input.Read<int>();

        return length switch {
            -1 => null,
            0 => string.Empty,
            _ => ReadString(input, length)
        };
    }
    
    public static string? ReadString(this Stream input, int length)
    {
        if (length < 64) {
            Span<byte> buffer = stackalloc byte[length];
            return ReadString(input, buffer);
        }
        
        using var rented = SpanOwner<byte>.Allocate(length);
        return ReadString(input, rented.Span);
    }
    
    public static string ReadString(this Stream input, Span<byte> dstBuffer)
    {
        var read = input.Read(dstBuffer);
        Debug.Assert(read == dstBuffer.Length);
        return Encoding.UTF8.GetString(dstBuffer);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteString(this Stream output, in string? value)
    {
        if (value is null) {
            output.Write(uint.MaxValue);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(value)) {
            output.Write(0u);
            return;
        }
        
        WriteString(output, value.AsSpan());
    }

    public static void WriteString(this Stream output, in ReadOnlySpan<char> value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        output.Write(byteCount);

        if (byteCount < 64) {
            Span<byte> utf8StackAlloc = stackalloc byte[byteCount];
            WriteString(output, value, utf8StackAlloc);
            return;
        }

        using var rented = SpanOwner<byte>.Allocate(byteCount);
        WriteString(output, value, rented.Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteString(in Stream output, ReadOnlySpan<char> text, Span<byte> dstByteBuffer)
    {
        Encoding.UTF8.GetBytes(text, dstByteBuffer);
        output.Write(dstByteBuffer);
    }
}