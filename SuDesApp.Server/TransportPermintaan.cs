using System.Net;
using Microsoft.AspNetCore.Http;

namespace SuDesApp.Server;

public static class TransportPermintaan
{
    /// <summary>
    /// HTTPS is mandatory except for loopback requests in Development. This
    /// check runs after forwarded headers, which are accepted only from a
    /// configured trusted proxy.
    /// </summary>
    public static bool Aman(HttpRequest request, IPAddress? remoteAddress, bool development) =>
        request.IsHttps || (development && remoteAddress is not null && IPAddress.IsLoopback(remoteAddress));
}
