using GameVault.Application.Catalogue;
using Microsoft.Extensions.Caching.Hybrid;

namespace GameVault.Infrastructure.Caching;

/// <summary>Decorator: adds HybridCache around the catalogue; remove it with one registration line.</summary>
public sealed class CachedGameCatalog : IGameCatalog
{
    public const string Tag = "catalogue";
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(5) };
    private static readonly string[] Tags = { Tag };

    private readonly IGameCatalog _inner;
    private readonly HybridCache _cache;

    public CachedGameCatalog(IGameCatalog inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery query, CancellationToken ct = default)
    {
        var n = query.Normalized();
        var key = $"search:{n.Search}|{n.Genre}|{n.Platform}|{n.Tag}|{n.Sort}|{n.Page}|{n.PageSize}";
        return await _cache.GetOrCreateAsync(key, async c => await _inner.SearchAsync(n, c), Options, Tags, ct);
    }

    public async Task<GameDetails?> GetDetailsAsync(string slug, CancellationToken ct = default)
        => await _cache.GetOrCreateAsync($"game:{slug.ToLowerInvariant()}", async c => await _inner.GetDetailsAsync(slug, c), Options, Tags, ct);

    public async Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default)
        => await _cache.GetOrCreateAsync("facets", async c => await _inner.GetFacetsAsync(c), Options, Tags, ct);

    public async Task<HomeModel> GetHomeAsync(CancellationToken ct = default)
        => await _cache.GetOrCreateAsync("home", async c => await _inner.GetHomeAsync(c), Options, Tags, ct);

    public async Task<IReadOnlyList<GameCard>> GetSimilarAsync(string slug, int count, CancellationToken ct = default)
        => await _cache.GetOrCreateAsync($"similar:{slug.ToLowerInvariant()}:{count}", async c => await _inner.GetSimilarAsync(slug, count, c), Options, Tags, ct);

    public async ValueTask InvalidateAsync(CancellationToken ct = default)
    {
        await _cache.RemoveByTagAsync(Tag, ct);
        await _inner.InvalidateAsync(ct);
    }
}
