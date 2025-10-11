using TkSharp.Core;
using TkSharp.Core.IO.ModSources;
using TkSharp.Core.Models;
using TkSharp.Merging;

namespace TkSharp.IO.Readers;

public sealed class FolderModReader(ITkSystemProvider systemProvider, ITkRomProvider romProvider) : ITkModReader
{
    private readonly ITkSystemProvider _systemProvider = systemProvider;
    private readonly ITkRomProvider _romProvider = romProvider;

    public async ValueTask<TkMod?> ReadMod(TkModContext context, CancellationToken ct = default)
    {
        if (context.Input is not string directory || !Directory.Exists(directory)) {
            return null;
        }

        context.EnsureId();
        
        FolderModSource source = new(directory);
        var writer = _systemProvider.GetSystemWriter(context);

        TkChangelogBuilder builder = new(source, writer, _romProvider.GetRom(),
            _systemProvider.GetSystemSource(context.Id.ToString())
        );
        
        var changelog = await builder.BuildAsync(ct)
            .ConfigureAwait(false);

        return new TkMod {
            Id = context.Id,
            Name = Path.GetFileName(directory),
            Changelog = changelog
        };
    }

    public bool IsKnownInput(object? input)
    {
        return input is string directory && Directory.Exists(directory);
    }
}