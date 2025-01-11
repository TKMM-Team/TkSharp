using System.Diagnostics;

namespace TkSharp.Extensions.GameBanana.Strategies;

public class SimpleDownload : IDownload
{
    public async Task<byte[]> GetBytesAndReportProgress(
        string url,
        HttpClient client,
        Func<IProgress<double>?> onStarted,
        Action onCompleted,
        Action<double> onProgress,
        Action<double> onSpeedUpdate,
        CancellationToken ct = default)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var progressReporter = onStarted();

        if (response.Content.Headers.ContentLength is not { } contentLength) {
            // If the length is not known ahead
            // of time, return the whole buffer
            onStarted();
            byte[] staticResult = await response.Content.ReadAsByteArrayAsync(ct);
            onCompleted();
            return staticResult;
        }

        const int frameBufferSize = 0x2000;

        byte[] result = new byte[contentLength];
        Memory<byte> buffer = result;
        int bytesRead = 0;

        var speedTimer = Stopwatch.StartNew();
        long bytesDownloadedInInterval = 0;

        using var speedReportTimer = new Timer(_ =>
        {
            var elapsedSeconds = speedTimer.Elapsed.TotalSeconds;
            if (elapsedSeconds > 0)
            {
                var bytesPerSecond = Interlocked.Exchange(ref bytesDownloadedInInterval, 0);
                var megabytesPerSecond = bytesPerSecond / (1024.0 * 1024.0);
                onSpeedUpdate(megabytesPerSecond);
                speedTimer.Restart();
            }
        }, null, 0, 1000);

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        while (bytesRead < contentLength) {
            int nextOffset = Math.Min(bytesRead + frameBufferSize, result.Length);
            int read = await stream.ReadAsync(buffer[bytesRead..nextOffset], ct);
            if (read == 0) break;

            bytesRead += read;
            Interlocked.Add(ref bytesDownloadedInInterval, read);
            
            var currentProgress = (double)bytesRead / contentLength;
            onProgress(currentProgress);
            progressReporter?.Report(currentProgress);
        }

        onCompleted();
        return result;
    }
} 