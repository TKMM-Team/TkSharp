using System.Text;

namespace TkSharp.Core.IO.Parsers;

public static class RegionLangMaskParser
{
    public static int ParseVersion(ReadOnlySpan<byte> src, out string nsoBinaryId)
    {
        StringBuilder sb = new();
        for (var i = src.Length - 1; i > -1; i--) {
            var @char = src[i];
            if (@char != '\n') {
                sb.Insert(0, (char)@char);
                continue;
            }

            if (i < 4) {
                goto InvalidInput;
            }
            
            nsoBinaryId = sb.ToString();
            return int.Parse(src[(i - 4)..(i - 1)]);
        }

    InvalidInput:
        throw new ArgumentException(
            "Invalid RegionLangMask contents: the version range could not be found.",
            nameof(src));
    }
}