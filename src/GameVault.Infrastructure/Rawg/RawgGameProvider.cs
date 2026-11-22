using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GameVault.Application.Metadata;
using GameVault.Domain;
using Microsoft.Extensions.Options;

namespace GameVault.Infrastructure.Rawg;

/// <summary>Typed client for RAWG. Retries, timeouts and the circuit breaker live in the HttpClient pipeline, not here.</summary>
public sealed class RawgGameProvider : IGameMetadataProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly string _key;

    public RawgGameProvider(HttpClient http, IOptions<RawgOptions> options)
    {
        _http = http;
        _key = options.Value.ApiKey;
    }

    public GameSource Source => GameSource.Rawg;

    public async Task<IReadOnlyList<ExternalGame>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"games?key={Uri.EscapeDataString(_key)}&page={page}&page_size={pageSize}&ordering=-added";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&search={Uri.EscapeDataString(query)}";

        var response = await _http.GetFromJsonAsync<RawgListResponse>(url, Json, ct);
        return response?.Results?.Select(r => RawgMapper.ToExternal(r)).OfType<ExternalGame>().ToList()
               ?? new List<ExternalGame>();
    }

    public async Task<ExternalGame?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(externalId);
        var key = Uri.EscapeDataString(_key);

        using var response = await _http.GetAsync($"games/{id}?key={key}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<RawgGameDto>(Json, ct);
        if (dto is null) return null;

        var shots = await _http.GetFromJsonAsync<RawgScreenshotsResponse>($"games/{id}/screenshots?key={key}", Json, ct);
        var urls = shots?.Results?.Select(s => s.Image).OfType<string>().ToList() ?? new List<string>();
        // A details row always counts as detailed, even when RAWG has no description for the game.
        return RawgMapper.ToExternal(dto with { DescriptionRaw = dto.DescriptionRaw ?? "" }, urls);
    }
}
