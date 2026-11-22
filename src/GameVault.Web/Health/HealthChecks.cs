using System.Text.Json;
using GameVault.Application.Metadata;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Health;

public static class HealthTags
{
    /// <summary>The site cannot work without these; a failure here means unhealthy.</summary>
    public const string Core = "core";

    /// <summary>The site degrades gracefully without these (stored copies are served), so a failure is only a warning.</summary>
    public const string External = "external";
}

/// <summary>
/// Is RAWG reachable? Any HTTP answer below 500 counts as reachable: the check sends no API key and does not go through the
/// request budget, so health probes never spend the monthly allowance. An outage is Degraded, never Unhealthy.
/// </summary>
public sealed class RawgHealthCheck : IHealthCheck
{
    public const string ClientName = "rawg-health";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _http;
    private readonly RawgOptions _options;
    private readonly TimeSpan _timeout;

    public RawgHealthCheck(IHttpClientFactory http, IOptions<RawgOptions> options, TimeSpan? timeout = null)
    {
        _http = http;
        _options = options.Value;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled) return HealthCheckResult.Healthy("RAWG integration is switched off.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var response = await _http.CreateClient(ClientName).GetAsync(_options.BaseAddress, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return (int)response.StatusCode < 500
                ? HealthCheckResult.Healthy("RAWG answered.")
                : HealthCheckResult.Degraded($"RAWG answered with {(int)response.StatusCode}; stored copies are served meanwhile.");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("RAWG is not reachable; stored copies are served meanwhile.");
        }
    }
}

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Names and statuses only. Exception text and connection strings never leave the server.</summary>
    public static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
