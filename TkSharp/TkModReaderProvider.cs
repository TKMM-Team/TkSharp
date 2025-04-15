using TkSharp.Core;
using TkSharp.IO.Readers;

namespace TkSharp;

public class TkModReaderProvider : ITkModReaderProvider
{
    private readonly List<ITkModReader> _readers;

    public TkModReaderProvider(ITkSystemProvider tkWriterProvider, ITkRomProvider tkRomProvider)
    {
        _readers = [
            new ArchiveModReader(tkWriterProvider, tkRomProvider, this),
            new FolderModReader(tkWriterProvider, tkRomProvider),
            new SevenZipModReader(tkWriterProvider, tkRomProvider, this),
            new TkPackReader(tkWriterProvider),
        ];
    }

    public void Register(ITkModReader reader)
    {
        // Use insert to ensure readers
        // added later take precedence
        _readers.Insert(0, reader);
    }

    public ITkModReader? GetReader(object input)
    {
        return _readers.FirstOrDefault(reader => reader.IsKnownInput(input));
    }

    public bool CanRead(object input)
    {
        return _readers.Any(reader => reader.IsKnownInput(input));
    }
}