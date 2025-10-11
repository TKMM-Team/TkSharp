using System.Globalization;
using CommunityToolkit.HighPerformance;
using TkSharp.Core.Extensions;

namespace TkSharp.Core;

public class TkCheat(string name) : Dictionary<string, uint[][]>
{
    public string Name { get; } = name;

    public static TkCheat FromText(Stream input, string name)
    {
        TkCheat result = new(name);

        string? key = null;
        List<uint[]> current = [];

        using StreamReader reader = new(input);
        while (reader.ReadLine() is string ln) {
            ReadOnlySpan<char> line = ln;
            if (line.Length == 0) {
                continue;
            }

            bool isKeyStart = line[0] is '[';

        ReadKey:
            if (isKeyStart && key is null) {
                if (line.LastIndexOf(']') is not (var index and > -1)) {
                    continue;
                }

                key = ln[1..index];
                continue;
            }

            if (key is null) {
                continue;
            }

            if (isKeyStart) {
                if (current.Count > 0) {
                    result[key] = current.ToArray();
                    current.Clear();
                }
                
                key = null;
                goto ReadKey;
            }

            List<uint> inLine = [];
            foreach (var range in line.Split(' ')) {
                var hex = line[range];
                if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out uint value)) {
                    continue;
                }

                inLine.Add(value);
            }

            if (inLine.Count > 0) {
                current.Add([..inLine]);
            }
        }
        
        if (key is not null && current.Count > 0) {
            result[key] = current.ToArray();
        }

        return result;
    }

    public static TkCheat FromBinary(Stream input)
    {
        string name = input.ReadString()!;
        TkCheat result = new(name);

        int count = input.Read<int>();

        for (int i = 0; i < count; i++) {
            string key = input.ReadString()!;
            int lineCount = input.Read<int>();

            List<uint[]> lines = new(lineCount);
            for (int ln = 0; ln < lineCount; ln++) {
                int valueCount = input.Read<int>();
                uint[] values = new uint[valueCount];

                for (int vi = 0; vi < valueCount; vi++) {
                    values[vi] = input.Read<uint>();
                }

                lines.Add(values);
            }

            result[key] = [.. lines];
        }

        return result;
    }

    public void WriteText(StreamWriter output)
    {
        bool isFirst = true;
        
        foreach ((string key, uint[][] values) in this) {
            if (!isFirst) {
                output.WriteLine();
            }
            
            isFirst = false;
            
            output.Write('[');
            output.Write(key);
            output.WriteLine(']');

            foreach (uint[] valuesInLine in values) {
                Span<uint> valuesInLineSpan = valuesInLine;
                foreach (uint value in valuesInLineSpan[..^1]) {
                    output.Write(value.ToString("X8"));
                    output.Write(' ');
                }

                output.WriteLine(valuesInLine[^1].ToString("X8"));
            }
        }
    }

    public void WriteBinary(Stream output)
    {
        output.WriteString(Name);
        output.Write(Count);

        foreach ((string key, uint[][] values) in this) {
            output.WriteString(key);
            output.Write(values.Length);

            foreach (uint[] valuesInLine in values) {
                output.Write(valuesInLine.Length);
                foreach (uint value in valuesInLine) {
                    output.Write(value);
                }
            }
        }
    }
}