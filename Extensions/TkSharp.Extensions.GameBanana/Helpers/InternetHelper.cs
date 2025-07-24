using System.Net.NetworkInformation;

namespace TkSharp.Extensions.GameBanana.Helpers;

public static class InternetHelper
{
    public static bool HasInternet => new Ping().Send("107.180.58.59", 1000).Status == IPStatus.Success;
}