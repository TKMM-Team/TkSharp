using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using TkSharp.Extensions.GameBanana.Models;

namespace TkSharp.Extensions.GameBanana.Strategies;

public class ThreadedDownloadStrategy(HttpClient client) : IDownloadStrategy
{
    private const int BUFFER_SIZE = 0x10000; // 64KB buffer
    private const int TIMEOUT_MS = 4000;
    private const long MB = 0x100000;

    public async ValueTask<byte[]> GetBytesAndReportProgress(Uri url, DownloadReporter? reporter, CancellationToken ct = default)
    {
        HttpResponseMessage? response = null;
        try {
            response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) {
                string errorMessage = response.StatusCode switch {
                    System.Net.HttpStatusCode.NotFound => 
                        "The requested file could not be found on the server.",
                    System.Net.HttpStatusCode.ServiceUnavailable => 
                        "The server is currently unavailable. Please try again later.",
                    System.Net.HttpStatusCode.GatewayTimeout => 
                        "The server took too long to respond. Please check your internet connection or try again later.",
                    _ => "The server returned an error. Please check your internet connection or try again."
                };
                
                throw new HttpRequestException(errorMessage, null, response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is not { } contentLength) {
                try {
                    return await response.Content.ReadAsByteArrayAsync(ct);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException) {
                    throw new HttpRequestException(
                        "Lost connection while downloading the file. Please check your internet connection and try again.", 
                        ex);
                }
            }

            byte[] result = new byte[contentLength];
            var downloadQueue = new ConcurrentQueue<(long start, long end)>();
            
            int segments = GetSegmentCount(contentLength);
            long segmentSize = (long)Math.Ceiling((double)contentLength / segments);
            long currentPosition = 0;

            for (int i = 0; i < segments && currentPosition < contentLength; i++) {
                long end = Math.Min(currentPosition + segmentSize - 1, contentLength - 1);
                downloadQueue.Enqueue((currentPosition, end));
                currentPosition += segmentSize;
            }

            long totalBytesDownloaded = 0;
            object @lock = new();
            var speedTimer = Stopwatch.StartNew();
            long bytesDownloadedInInterval = 0;

            await using var speedReportTimer = new Timer(_ => {
                double elapsedSeconds = speedTimer.Elapsed.TotalSeconds;
                if (elapsedSeconds > 0) {
                    long bytesInInterval = Interlocked.Exchange(ref bytesDownloadedInInterval, 0);
                    double bytesPerSecond = (bytesInInterval / elapsedSeconds);
                    double megabytesPerSecond = bytesPerSecond / MB;
                    reporter?.ReportSpeed(megabytesPerSecond);
                    speedTimer.Restart();
                }
            }, null, 0, 1000);

            var failures = new ConcurrentDictionary<int, string>();
            var downloadTasks = new Task[segments];

            for (int i = 0; i < segments; i++) {
                int segmentIndex = i;
                downloadTasks[i] = Task.Run<Task>(async () => {
                    while (downloadQueue.TryDequeue(out (long start, long end) segment)) {
                        long start = segment.start;
                        long end = segment.end;
                        long expectedBytes = end - start + 1;

                        int attempt = 0;
                        const int maxRetry = 5;
                        bool success = false;
                        int consecutiveTimeouts = 0;

                        while (attempt < maxRetry && !success) {
                            try {
                                long segmentBytesRead = 0;
                                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                                long resumePosition = start + segmentBytesRead;
                                request.Headers.Range = new RangeHeaderValue(resumePosition, end);

                                using HttpResponseMessage segmentResponse = await client.SendAsync(
                                    request, HttpCompletionOption.ResponseHeadersRead, ct);
                                
                                if (!segmentResponse.IsSuccessStatusCode) {
                                    attempt++;
                                    if (attempt < maxRetry) {
                                        await Task.Delay(100 * attempt, ct);
                                        continue;
                                    }
                                    
                                    failures.TryAdd(segmentIndex, 
                                        $"Segment {segmentIndex + 1} failed with status code {segmentResponse.StatusCode}");
                                    break;
                                }

                                await using Stream responseStream = await segmentResponse.Content.ReadAsStreamAsync(ct);
                                byte[] buffer = new byte[Math.Min(BUFFER_SIZE, expectedBytes - segmentBytesRead)];

                                while (segmentBytesRead < expectedBytes) {
                                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                    timeoutCts.CancelAfter(TIMEOUT_MS);

                                    int bytesRead = await responseStream.ReadAsync(
                                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, expectedBytes - segmentBytesRead)),
                                        timeoutCts.Token);

                                    if (bytesRead == 0) {
                                        consecutiveTimeouts++;
                                        if (consecutiveTimeouts >= maxRetry)
                                        {
                                            throw new TimeoutException(
                                                $"Segment {segmentIndex + 1}: No data received for {consecutiveTimeouts * 4} seconds. " +
                                                "This might indicate a network or server issue. Please check your connection or try again later.");
                                        }
                                        
                                        continue;
                                    }

                                    consecutiveTimeouts = 0;

                                    lock (@lock) {
                                        Array.Copy(buffer, 0, result, start + segmentBytesRead, bytesRead);
                                        totalBytesDownloaded += bytesRead;
                                        Interlocked.Add(ref bytesDownloadedInInterval, bytesRead);
                                        segmentBytesRead += bytesRead;

                                        double currentProgress = (double)totalBytesDownloaded / contentLength;
                                        reporter?.ReportProgress(currentProgress);
                                    }
                                }

                                success = segmentBytesRead == expectedBytes;
                            }
                            catch {
                                attempt++;
                                if (attempt < maxRetry) {
                                    await Task.Delay(100 * attempt, ct);
                                }
                            }
                        }

                        if (!success) {
                            throw new HttpRequestException(
                                $"Download segment {segmentIndex + 1} failed after {maxRetry} attempts. " +
                                "Please check your internet connection and try again.");
                        }
                    }

                    return Task.CompletedTask;
                }, ct).Unwrap();
            }

            try {
                await Task.WhenAll(downloadTasks);
            }
            catch (Exception) {
                if (!failures.IsEmpty) {
                    throw new HttpRequestException(
                        $"Download failed: {failures.Count} of {segments} segments failed to download.\n" +
                        $"Details:\n{string.Join("\n", failures.Values)}\n" +
                        "Please check your internet connection and try again.");
                }
                throw;
            }

            return result;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException) {
            throw new HttpRequestException(
                "Unable to establish connection to the server. " +
                "Please check your internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) {
            throw new OperationCanceledException(
                "Download was cancelled.", ex);
        }
        catch (OperationCanceledException ex) {
            throw new HttpRequestException(
                "The initial connection to the server timed out. " +
                "This could be due to slow internet or server issues.", ex);
        }
        catch (Exception ex) {
            throw new HttpRequestException(
                "An unexpected error occurred during the download. " +
                "Please check your internet connection and try again.", ex);
        }
        finally {
            response?.Dispose();
        }
    }

    private static int GetSegmentCount(long fileSize)
    {
        return fileSize switch {
            < 5 * MB => 1,
            < 10 * MB => 2,
            < 20 * MB => 4,
            < 30 * MB => 6,
            < 40 * MB => 8,
            _ => 10
        };
    }
}