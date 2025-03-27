using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp.Merging;

public class TkOptimizerBuilder
{
    private readonly ITkModSource _source;
    private readonly ITkModWriter _writer;
    private readonly Dictionary<string, TkChangelogEntry> _entries = [];
    private readonly TkChangelog _changelog;

    public TkOptimizerBuilder(ITkModSource source, ITkModWriter writer, ITkSystemSource? systemSource)
    {
        _source = source;
        _writer = writer;
        _changelog = new TkChangelog {
            BuilderVersion = 100,
            GameVersion = 121,
            Source = systemSource
        };
    }

    public async ValueTask<TkChangelog> BuildMetadataAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(_source.Files
            .Select(file => Task.Run(() => BuildTarget(file.FilePath, file.Entry), ct)))
            .ConfigureAwait(false);

        InsertEntries();
        return _changelog;
    }

    private void BuildTarget(string file, object entry)
    {
        TkPath path = TkPath.FromPath(file, _source.PathToRoot, out bool isInvalid);
        if (isInvalid) {
            return;
        }

        string canonical = path.Canonical.ToString();
        using Stream content = _source.OpenRead(entry);

        switch (path) {
            case { Root: "exefs", Extension: ".ips" }:
                if (TkPatch.FromIps(content, path.Canonical[..^4].ToString()) is TkPatch patch) {
                    _changelog.PatchFiles.Add(patch);
                }
                return;

            case { Root: "exefs", Extension: ".pchtxt" }:
                if (TkPatch.FromPchTxt(content) is TkPatch patchFromPchtxt) {
                    _changelog.PatchFiles.Add(patchFromPchtxt);
                }
                return;

            case { Root: "exefs", Canonical.Length: 7 } when path.Canonical[..6] is "subsdk":
                _changelog.SubSdkFiles.Add(canonical);
                goto Copy;

            case { Root: "exefs" }:
                _changelog.ExeFiles.Add(canonical);
                goto Copy;

            case { Root: "cheats" }:
                if (TkCheat.FromText(content, Path.GetFileNameWithoutExtension(canonical)) is var cheat and { Count: > 0 }) {
                    _changelog.CheatFiles.Add(cheat);
                }
                return;

            case { Root: "romfs" or "extras" }:
                goto Copy;
        }

        return;

    Copy:
        using (Stream output = _writer.OpenWrite(Path.Combine(path.Root.ToString(), canonical))) {
            content.CopyTo(output);
        }
    }

    private void InsertEntries()
    {
        _changelog.ChangelogFiles.AddRange(_entries.Values);
    }
}