// Probe healthcheck Docker: TCP connect ke /healthz, exit 0 bila HTTP 200.
using System.Net.Sockets;

var port = int.TryParse(Environment.GetEnvironmentVariable("HEALTHCHECK_PORT"), out var p) ? p : 8080;
try
{
    using var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", port);
    await using var stream = client.GetStream();
    await using var writer = new StreamWriter(stream) { AutoFlush = true };
    await writer.WriteLineAsync("GET /healthz HTTP/1.0");
    await writer.WriteLineAsync();
    using var reader = new StreamReader(stream);
    var status = await reader.ReadLineAsync() ?? "";
    return status.Contains("200") ? 0 : 1;
}
catch
{
    return 1;
}
