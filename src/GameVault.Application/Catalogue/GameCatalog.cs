namespace GameVault.Application.Catalogue;

public sealed class GameCatalog : IGameCatalog
{
    public const int SimilarPoolSize = 200;
    private const int HomeSection = 8;

    private readonly IGameRepository _games;
    private readonly ISimilarityStrategy _similarity;

    public GameCatalog(IGameRepository games, ISimilarityStrategy similarity)
    {
        _games = games;
        _similarity = similarity;
    }

    public Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery query, CancellationToken ct = default)
        => _games.SearchAsync(query.Normalized(), ct);

    public Task<GameDetails?> GetDetailsAsync(string slug, CancellationToken ct = default)
        => _games.GetBySlugAsync(slug.ToLowerInvariant(), ct);

    public Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default) => _games.GetFacetsAsync(ct);

    public Task<HomeModel> GetHomeAsync(CancellationToken ct = default) => _games.GetHomeAsync(HomeSection, ct);

    public async Task<IReadOnlyList<GameCard>> GetSimilarAsync(string slug, int count, CancellationToken ct = default)
    {
        slug = slug.ToLowerInvariant();
        var source = await _games.GetCandidateAsync(slug, ct);
        if (source is null) return Array.Empty<GameCard>();
        var pool = await _games.GetCandidatePoolAsync(slug, SimilarPoolSize, ct);

        // Ranking 200 candidates takes about 20 microseconds (benchmarks/GameVault.Benchmarks, docs/PERFORMANCE.md). A bounded
        // top-N selection and a tuned set comparison were both measured and not worth their complexity: loading the candidates
        // from SQL is hundreds of times more expensive than ranking them.
        return pool
            .Select(c => (c.Card, Score: _similarity.Score(source, c)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Card.RatingAverage)
            .Take(count)
            .Select(x => x.Card)
            .ToList();
    }

    public ValueTask InvalidateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
