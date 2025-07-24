using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Revrs;
using Revrs.Extensions;

namespace TkSharp.Core;

public class TkPatch(string nsoBinaryId)
{
    private const uint MOV_W8_NO_VALUE_BE = 0x52800008;

    private const string NSO_BINARY_ID_110 = "d5ad6ac71ef53e3e52417c1b81dbc9b4142aa3b3";
    private const string NSO_BINARY_ID_111 = "168dd518d925c7a327677286e72feda833314919";
    private const string NSO_BINARY_ID_112 = "9a10ed9435c06733da597d8094d9000ab5d3ee60";
    private const string NSO_BINARY_ID_120 = "6f32c68dd3bc7d77aa714b80e92a096a737cda77";
    private const string NSO_BINARY_ID_121 = "9b4e43650501a4d4489b4bbfdb740f26af3cf850";
    private const string NSO_BINARY_ID_140 = "6265f94d606242ce730ef721a8037dda8e4bfc63";
    private const string NSO_BINARY_ID_141 = "965eab9ceb8eb867f747da772022c95065c9b927";
    private const string NSO_BINARY_ID_142 = "5cb42b1cf25469fb0635fd046453d843c18bc8ab";

    private const uint IPS32_MAGIC_PREFIX = 0x33535049;
    private const byte IPS32_MAGIC_SUFFIX = 0x32;
    private const uint EOF_MARK = 0x45454F46;
    private const uint NSO_HEADER_LENGTH = 0x100;

    private static readonly Dictionary<string, uint> _shopParamPatchAddresses = new() {
        [NSO_BINARY_ID_110] = 0x01ada148,
        [NSO_BINARY_ID_111] = 0x01ad7938,
        [NSO_BINARY_ID_112] = 0x01ace2b8,
        [NSO_BINARY_ID_120] = 0x01ac0128,
        [NSO_BINARY_ID_121] = 0x01acb308,
        [NSO_BINARY_ID_140] = 0x0212e60c,
        [NSO_BINARY_ID_141] = 0x0211e2b8,
        [NSO_BINARY_ID_142] = 0x0211de70,
    };

    public static TkPatch CreateWithDefaults(string nsoBinaryId, uint shopParamLimit = 512)
    {
        TkPatch result = new(nsoBinaryId);
        
        if (_shopParamPatchAddresses.TryGetValue(nsoBinaryId, out uint shopParamPatchAddress)) {
            uint beByteCode = MOV_W8_NO_VALUE_BE | (shopParamLimit << 5);
            uint leByteCode = BinaryPrimitives.ReverseEndianness(beByteCode);
            result.Entries[shopParamPatchAddress] = leByteCode;
        }
        
        return result;
    }

    public string NsoBinaryId { get; } = nsoBinaryId;

    public Dictionary<uint, uint> Entries { get; } = [];

    public void WriteIps(Stream output)
    {
        output.Write(IPS32_MAGIC_PREFIX);
        output.Write(IPS32_MAGIC_SUFFIX);

        foreach ((uint address, uint value) in Entries) {
            output.Write(address + NSO_HEADER_LENGTH, Endianness.Big);
            output.Write<short>(sizeof(uint), Endianness.Big);
            output.Write(value, Endianness.Big);
        }

        output.Write(EOF_MARK, Endianness.Big);
    }

    public static TkPatch? FromIps(Stream stream, string nsoBinaryId)
    {
        if (stream.Read<uint>() is not IPS32_MAGIC_PREFIX || stream.ReadByte() is not IPS32_MAGIC_SUFFIX) {
            return null;
        }

        TkPatch patch = new(nsoBinaryId);

        uint address = stream.Read<uint>(Endianness.Big);
        while (address is not EOF_MARK && stream.Position < stream.Length) {
            int valueSize = stream.Read<short>(Endianness.Big);
            if (valueSize is not 4) {
                goto NextAddress;
            }

            uint value = stream.Read<uint>(Endianness.Big);
            patch.Entries[address - NSO_HEADER_LENGTH] = value;

        NextAddress:
            address = stream.Read<uint>(Endianness.Big);
        }

        return patch;
    }

    private const string NSOBID_KEYWORD = "@nsobid";
    private const string ENABLED_KEYWORD = "@enabled";
    private const char COMMENT_CHAR = '@';
    private const string STOP_KEYWORD = "@stop";

    public static TkPatch? FromPchTxt(Stream stream)
    {
        using StreamReader reader = new(stream);

        if (reader.ReadLine() is not string firstLine) {
            return null;
        }

        if (!TryGetNsoBinaryId(firstLine, out string? nsoBinaryId)) {
            return null;
        }

        TkPatch patch = new(nsoBinaryId);

        var state = State.None;
        while (reader.ReadLine() is string line) {
            if (state is State.Enabled) {
                if (line.StartsWith(STOP_KEYWORD)) {
                    state = State.None;
                    continue;
                }

                if (line.Length > 0 && line[0] == COMMENT_CHAR) {
                    continue;
                }

                if (GetAddressAndValue(line) is not { } values) {
                    continue;
                }

                patch.Entries[values.Address] = values.Value;
                continue;
            }

            if (line.StartsWith(ENABLED_KEYWORD)) {
                state = State.Enabled;
            }
        }

        return patch;
    }

    private static bool TryGetNsoBinaryId(ReadOnlySpan<char> line, [MaybeNullWhen(false)] out string nosBinaryId)
    {
        if (line is not { Length: > 7 } || line[..7] is not NSOBID_KEYWORD) {
            nosBinaryId = null;
            return false;
        }

        nosBinaryId = line[8..].ToString();
        return true;
    }

    private static (uint Address, uint Value)? GetAddressAndValue(ReadOnlySpan<char> line)
    {
        int addressEndIndex = GetValueEndIndex(line, 0);
        if (!uint.TryParse(line[..addressEndIndex], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out uint address)) {
            return null;
        }

        int valueEndIndex = GetValueEndIndex(line, ++addressEndIndex);
        if (!uint.TryParse(line[addressEndIndex..valueEndIndex], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out uint value)) {
            return null;
        }

        return (address, value);
    }

    private static int GetValueEndIndex(ReadOnlySpan<char> chars, int startIndex)
    {
        int endIndex = startIndex;
        while (endIndex < chars.Length && chars[endIndex] is >= 'A' and <= 'F' or >= 'a' and <= 'f' or >= '0' and <= '9') {
            endIndex++;
        }

        return endIndex;
    }
}

file enum State
{
    None,
    Enabled,
}