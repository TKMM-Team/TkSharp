using System.Net;

namespace TkSharp.Extensions.GameBanana.Strategies;

public interface IDownload
{
    Task<byte[]> GetBytesAndReportProgress(
        string url, 
        HttpClient client,
        Func<IProgress<double>?> onStarted,
        Action onCompleted,
        Action<double> onProgress,
        Action<double> onSpeedUpdate,
        CancellationToken ct = default);
} 