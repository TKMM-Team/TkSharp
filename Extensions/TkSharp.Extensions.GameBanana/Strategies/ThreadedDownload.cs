using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace TkSharp.Extensions.GameBanana.Strategies;

public class ThreadedDownload : IDownload
{
    private const int SEGMENTS = 7;
    private const int BUFFER_SIZE = 0x10000; // 64KB buffer
    private const int TIMEOUT_MS = 4000;

    public async Task<byte[]> GetBytesAndReportProgress(
        string url,
        HttpClient client,
        Func<IProgress<double>?> onStarted,
        Action onCompleted,
        Action<double> onProgress,
        Action<double> onSpeedUpdate,
        CancellationToken ct = default)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is not { } contentLength)
        {
            onStarted();
            byte[] staticResult = await response.Content.ReadAsByteArrayAsync(ct);
            onCompleted();
            return staticResult;
        }

        byte[] result = new byte[contentLength];
        var downloadQueue = new ConcurrentQueue<(int segmentIndex, long start, long end)>();

        // Calculate segment sizes ensuring no bytes are missed
        long segmentSize = (long)Math.Ceiling((double)contentLength / SEGMENTS);
        long currentPosition = 0;

        for (int i = 0; i < SEGMENTS && currentPosition < contentLength; i++)
        {
            long end = Math.Min(currentPosition + segmentSize - 1, contentLength - 1);
            downloadQueue.Enqueue((i, currentPosition, end));
            currentPosition += segmentSize;
        }

        long totalBytesDownloaded = 0;
        long verifiedBytesDownloaded = 0;
        object lockObject = new object();
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

        IProgress<double>? progressReporter = onStarted();

        var downloadTasks = new Task[SEGMENTS];
        for (int i = 0; i < SEGMENTS; i++)
        {
            downloadTasks[i] = Task.Run<Task>(async () =>
            {
                while (downloadQueue.TryDequeue(out var segment))
                {
                    int segmentIndex = segment.segmentIndex;
                    long start = segment.start;
                    long end = segment.end;
                    long expectedBytes = end - start + 1;

                    int attempt = 0;
                    const int maxRetry = 5;
                    bool success = false;

                    while (attempt < maxRetry && !success)
                    {
                        try
                        {
                            long segmentBytesRead = 0;
                            using var request = new HttpRequestMessage(HttpMethod.Get, url);
                            long resumePosition = start + segmentBytesRead;
                            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumePosition, end);

                            using var segmentResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                            if (!segmentResponse.IsSuccessStatusCode)
                            {
                                attempt++;
                                if (attempt < maxRetry)
                                {
                                    await Task.Delay(100 * attempt, ct);
                                }
                                continue;
                            }

                            await using var responseStream = await segmentResponse.Content.ReadAsStreamAsync(ct);
                            byte[] buffer = new byte[Math.Min(BUFFER_SIZE, expectedBytes - segmentBytesRead)];

                            while (segmentBytesRead < expectedBytes)
                            {
                                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                timeoutCts.CancelAfter(TIMEOUT_MS);

                                int bytesRead = await responseStream.ReadAsync(
                                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, expectedBytes - segmentBytesRead)),
                                    timeoutCts.Token);

                                if (bytesRead == 0) break;

                                lock (lockObject)
                                {
                                    Array.Copy(buffer, 0, result, start + segmentBytesRead, bytesRead);
                                    totalBytesDownloaded += bytesRead;
                                    Interlocked.Add(ref bytesDownloadedInInterval, bytesRead);
                                    segmentBytesRead += bytesRead;

                                    double currentProgress = (double)totalBytesDownloaded / contentLength;
                                    onProgress(currentProgress);
                                    progressReporter?.Report(currentProgress);
                                }
                            }

                            success = segmentBytesRead == expectedBytes;
                            if (!success)
                            {
                                attempt++;
                                if (attempt < maxRetry)
                                {
                                    await Task.Delay(100 * attempt, ct);
                                }
                            }
                        }
                        catch
                        {
                            attempt++;
                            if (attempt < maxRetry)
                            {
                                await Task.Delay(100 * attempt, ct);
                            }
                        }
                    }
                }
                return Task.CompletedTask;
            }, ct).Unwrap();
        }

        await Task.WhenAll(downloadTasks);

        onCompleted();
        return result;
    }
}
