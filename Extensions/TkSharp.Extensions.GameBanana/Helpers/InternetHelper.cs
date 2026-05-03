using System.Net.NetworkInformation;

namespace TkSharp.Extensions.GameBanana.Helpers;

public static class InternetHelper
{
    public static bool HasInternet {
        get {
            try {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch {
                return false;
            }
        }
    }
}