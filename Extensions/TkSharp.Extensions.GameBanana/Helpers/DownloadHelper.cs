using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using TkSharp.Extensions.GameBanana.Models;
using TkSharp.Extensions.GameBanana.Strategies;

namespace TkSharp.Extensions.GameBanana.Helpers;

public static class DownloadHelper
{
    public static readonly DownloadConfig Config = DownloadConfig.Load();

    public static readonly HttpClient Client = new() {
        Timeout = TimeSpan.FromSeconds(Config.TimeoutSeconds)
    };

    static DownloadHelper() {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("tkmm-client-application");
        Client.DefaultRequestHeaders.Accept.Clear();
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    }

    public static Stack<DownloadReporter> Reporters { get; } = new();
    
    public static event Func<Task> OnDownloadStarted = () => Task.CompletedTask;
    public static event Func<Task> OnDownloadCompleted = () => Task.CompletedTask;

    public static Task<byte[]> DownloadAndVerify(string fileUrl, byte[] md5Checksum, CancellationToken ct = default)
    {
        return DownloadAndVerify(new Uri(fileUrl), md5Checksum, ct);
    }
    
    public static async Task<byte[]> DownloadAndVerify(Uri fileUrl, byte[] md5Checksum, CancellationToken ct = default)
    {
        IDownloadStrategy strategy = Config.UseThreadedDownloads
            ? new ThreadedDownloadStrategy(Client)
            : new SimpleDownloadStrategy(Client);

        int maxRetry = Config.MaxRetries;
        int attempt = 0;
        
        byte[] data;
        byte[] hash;

        do {
        Retry:
            if (maxRetry < attempt) {
                throw new HttpRequestException($"Failed to download resource. The max retry of {maxRetry} was exceeded.",
                    inner: null, HttpStatusCode.BadRequest
                );
            }

            try {
                await OnDownloadStarted();
                data = await strategy.GetBytesAndReportProgress(fileUrl, Reporters.TryPeek(out var reporter) ? reporter : null, ct);
                hash = MD5.HashData(data);
            }
            catch (HttpRequestException ex) {
                if (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestTimeout) {
                    goto Retry;
                }

                throw;
            }
            finally {
                attempt++;
            }
        } while (hash.SequenceEqual(md5Checksum) is false);

        await OnDownloadCompleted();
        return data;
    }
}