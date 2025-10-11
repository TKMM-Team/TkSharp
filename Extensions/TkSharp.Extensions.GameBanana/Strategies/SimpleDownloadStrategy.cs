using System.Diagnostics;
using System.Net.Sockets;
using TkSharp.Extensions.GameBanana.Models;

namespace TkSharp.Extensions.GameBanana.Strategies;

public class SimpleDownloadStrategy(HttpClient client) : IDownloadStrategy
{
    private const int FRAME_BUFFER_SIZE = 0x2000;
    
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
                        "Lost connection while downloading. Please check your internet connection and try again.", 
                        ex);
                }
            }

            byte[] result = new byte[contentLength];
            Memory<byte> buffer = result;
            int bytesRead = 0;

            int bytesReadAtLastFrame = 0;
            long startTime = Stopwatch.GetTimestamp();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            
            while (bytesRead < contentLength) {
                try {
                    int nextOffset = Math.Min(bytesRead + FRAME_BUFFER_SIZE, result.Length);
                    int read = await stream.ReadAsync(buffer[bytesRead..nextOffset], ct);
                    if (read == 0) {
                        if (bytesRead < contentLength) {
                            throw new IOException(
                                "Download was interrupted. Please check your internet connection and try again.");
                        }
                        break;
                    }

                    bytesRead += read;

                    if (reporter is not null && Stopwatch.GetElapsedTime(startTime).TotalSeconds >= 1) {
                        double bytesPerSecond = bytesRead - bytesReadAtLastFrame;
                        reporter.ReportSpeed(bytesPerSecond / (1024.0 * 1024.0));
                        bytesReadAtLastFrame = bytesRead;
                        startTime = Stopwatch.GetTimestamp();
                    }

                    reporter?.ReportProgress((double)bytesRead / contentLength);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException) {
                    throw new HttpRequestException(
                        $"Lost connection after downloading {bytesRead:N0} bytes out of {contentLength:N0} bytes. " +
                        "Please check your internet connection and try again.", 
                        ex);
                }
            }

            return result;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException) {
            throw new HttpRequestException(
                "Unable to connect to the server. " +
                "Please check your internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) {
            throw new OperationCanceledException(
                "Download was cancelled.", ex);
        }
        catch (OperationCanceledException ex) {
            throw new HttpRequestException(
                "The server took too long to respond." +
                " This might be due to a slow internet connection or server issues.", ex);
        }
        catch (Exception ex) when (ex is not HttpRequestException) {
            throw new HttpRequestException(
                "An unexpected error occurred while downloading. " +
                "Please check your internet connection and try again.", ex);
        }
        finally {
            response?.Dispose();
        }
    }
}