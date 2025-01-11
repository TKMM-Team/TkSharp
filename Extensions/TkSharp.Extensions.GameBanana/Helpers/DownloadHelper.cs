using System.Net;
using System.Security.Cryptography;
using TkSharp.Extensions.GameBanana.Strategies;

namespace TkSharp.Extensions.GameBanana.Helpers;

public static class DownloadHelper
{
    public static event Func<IProgress<double>?> OnDownloadStarted = () => null;
    public static event Action OnDownloadCompleted = () => { };
    public static event Action<double> OnSpeedUpdate = _ => { };

    private static readonly HttpClient _client = new() {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public static HttpClient Client => _client;

    public static double Progress { get; private set; }
    public static double Speed { get; private set; }

    public static Func<bool>? ThreadedDownloadsEnabled { private get; set; }

    private static bool UseThreadedDownloads => ThreadedDownloadsEnabled?.Invoke() ?? false;

    public static async Task<byte[]> DownloadAndVerify(
        string fileUrl, 
        byte[] md5Checksum, 
        int maxRetry = 5, 
        CancellationToken ct = default)
    {
        IDownload strategy = UseThreadedDownloads
            ? new ThreadedDownload()
            : new SimpleDownload();

        int retry = 0;
        byte[] data;
        byte[] hash;

        do {
            Retry:
            if (maxRetry < retry) {
                throw new HttpRequestException($"Failed to download resource. The max retry of {maxRetry} was exceeded.",
                    inner: null,
                    HttpStatusCode.BadRequest
                );
            }

            try {
                data = await strategy.GetBytesAndReportProgress(
                    fileUrl, 
                    _client,
                    OnDownloadStarted,
                    OnDownloadCompleted,
                    progress => Progress = progress,
                    speed => {
                        Speed = speed;
                        OnSpeedUpdate(speed);
                    },
                    ct);
                    
                hash = MD5.HashData(data);
            }
            catch (HttpRequestException ex) {
                if (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestTimeout) {
                    goto Retry;
                }

                throw;
            }
            finally {
                retry++;
            }
        } while (hash.SequenceEqual(md5Checksum) == false);

        return data;
    }
}