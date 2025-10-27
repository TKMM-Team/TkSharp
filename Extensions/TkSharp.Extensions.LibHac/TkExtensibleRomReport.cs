using System.Text;
using LibHac.Common.Keys;

namespace TkSharp.Extensions.LibHac;

public readonly ref struct TkExtensibleRomReport(
    KeySet? keys,
    bool hasGame,
    Dictionary<string, bool> baseGameSources,
    bool hasUpdate,
    Dictionary<string, bool> updateSources)
{
    public readonly KeySet? Keys = keys;

    public bool HasKeys => Keys is not null;

    public readonly bool HasGame = hasGame;
    
    public readonly Dictionary<string, bool> BaseGameSources = baseGameSources;

    public readonly bool HasUpdate = hasUpdate;
    
    public readonly Dictionary<string, bool> UpdateSources = updateSources;

    public string PrintMarkdown()
    {
        StringBuilder sb = new();

        sb.AppendLine("# Configuration Report");
        sb.AppendLine();
        
        sb.Append(HasKeys ? "- `[x]` " : "- `[ ]` ");
        sb.AppendLine("Keys Found");
        sb.AppendLine();

        sb.Append("## Base Game is ");
        sb.AppendLine(HasGame ? "Found" : "Missing");
        sb.AppendLine();

        foreach ((var source, var state) in BaseGameSources) {
            sb.AppendLine($"- `[{(state ? "x" : " ")}]` {source}");
        }

        sb.AppendLine();
        sb.Append("## Update is ");
        sb.AppendLine(HasUpdate ? "Found" : "Missing");
        sb.AppendLine();
        
        foreach ((var source, var state) in UpdateSources) {
            sb.AppendLine($"- `[{(state ? "x" : " ")}]` {source}");
        }
        
        return sb.ToString();
    }
}