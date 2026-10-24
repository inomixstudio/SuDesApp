using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SuDesApp.Server;

/// <summary>
/// Restricts forwarded client metadata to explicitly trusted reverse proxies.
/// The framework's loopback defaults remain trusted for local development.
/// </summary>
public static class ProxyForwarding
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        AddAddresses(options, configuration["SUDES_TRUSTED_PROXY_IPS"] ?? configuration["ForwardedHeaders:KnownProxies"]);
        AddNetworks(options, configuration["SUDES_TRUSTED_PROXY_NETWORKS"] ?? configuration["ForwardedHeaders:KnownIPNetworks"]);
    }

    private static void AddAddresses(ForwardedHeadersOptions options, string? values)
    {
        foreach (var value in Entries(values))
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException($"Alamat proxy tepercaya tidak sah: '{value}'.");

            options.KnownProxies.Add(address);
        }
    }

    private static void AddNetworks(ForwardedHeadersOptions options, string? values)
    {
        foreach (var value in Entries(values))
        {
            if (!System.Net.IPNetwork.TryParse(value, out var network))
                throw new InvalidOperationException($"Jaringan proxy tepercaya tidak sah: '{value}'.");

            options.KnownIPNetworks.Add(network);
        }
    }

    private static string[] Entries(string? values) =>
        (values ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
