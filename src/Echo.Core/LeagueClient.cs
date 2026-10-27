using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Echo.Core;

public sealed class LeagueClient : IDisposable
{
    private readonly HttpClient _client;
    public LeagueClient()
    {
        var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = ValidateCertificate;
        _client = new(handler) { BaseAddress = new("https://127.0.0.1:2999/liveclientdata/"), Timeout = TimeSpan.FromSeconds(2) };
    }
    private static bool ValidateCertificate(HttpRequestMessage request, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        // League uses a local self-signed certificate. Never relax TLS for any remote host,
        // and never follow redirects from this loopback-only client.
        return request.RequestUri is { Host: "127.0.0.1", Port: 2999, Scheme: "https" } && cert != null;
    }
    public async Task<JsonDocument> GetAsync(string endpoint, CancellationToken ct) =>
        JsonDocument.Parse(await _client.GetStringAsync(endpoint, ct).ConfigureAwait(false));
    public void Dispose() => _client.Dispose();
}
