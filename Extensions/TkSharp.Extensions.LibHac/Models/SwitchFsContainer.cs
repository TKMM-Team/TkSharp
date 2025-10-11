using System.Diagnostics.Contracts;
using LibHac.Tools.Fs;

namespace TkSharp.Extensions.LibHac.Models;

internal sealed class SwitchFsContainer : List<(string Label, SwitchFs Fs)>, IDisposable
{
    private readonly List<IDisposable> _cleanup = [];
    
    [Pure]
    public IEnumerable<SwitchFs> AsFsList() => this.Select(s => s.Fs);

    public void CleanupLater(IDisposable fs)
    {
        _cleanup.Add(fs);
    }
    
    public void Dispose()
    {
        foreach (var (_, fs) in this) {
            fs.Dispose();
        }
        
        foreach (var disposable in _cleanup) {
            disposable.Dispose();
        }
    }
}