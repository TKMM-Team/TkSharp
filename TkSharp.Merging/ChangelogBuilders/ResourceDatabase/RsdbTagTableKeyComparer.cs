namespace TkSharp.Merging.ChangelogBuilders.ResourceDatabase;

public sealed class RsdbTagTableKeyComparer : IComparer<(string Prefix, string Name, string Suffix)>
{
    public static readonly RsdbTagTableKeyComparer Instance = new();

    public int Compare((string Prefix, string Name, string Suffix) x, (string Prefix, string Name, string Suffix) y)
    {
        var prefixComparison = StringComparer.Ordinal.Compare(x.Prefix, y.Prefix);
        int nameComparison;

        return prefixComparison switch {
            0 => (nameComparison = StringComparer.Ordinal.Compare(x.Name, y.Name)) switch {
                0 => StringComparer.Ordinal.Compare(x.Suffix, y.Suffix),
                _ => nameComparison
            },
            _ => prefixComparison
        };
    }
}