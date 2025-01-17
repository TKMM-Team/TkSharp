using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.Tools.FsSystem;
using LibHac.Tools.Fs;
using TkSharp.Extensions.LibHac.Extensions;

namespace TkSharp.Extensions.LibHac.Helpers
{
    public class SplitRomHelper : ILibHacRomHelper
    {
        private ConcatenationStorage? _storage;

        public SwitchFs Initialize(string splitDirectory, KeySet keys)
        {
            IList<IStorage> splitFiles = [.. Directory.EnumerateFiles(splitDirectory)
                .OrderBy(f => f)
                .Select(f => new LocalStorage(f, FileAccess.Read))];

            _storage = new ConcatenationStorage(splitFiles, true);
            return _storage.GetSwitchFs("rom", keys);
        }

        public void Dispose()
        {
            _storage?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
} 