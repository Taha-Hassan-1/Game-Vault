namespace GameVault.Application.Catalogue;

public interface IGameRepository
{
    Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery query, CancellationToken ct = default);
    Task<GameDetails?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default);
    Task<HomeModel> GetHomeAsync(int perSection, CancellationToken ct = default);
    Task<SimilarityCandidate?> GetCandidateAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<SimilarityCandidate>> GetCandidatePoolAsync(string excludeSlug, int poolSize, CancellationToken ct = default);
}

public interface IGameCatalog
{
    Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery query, CancellationToken ct = default);
    Task<GameDetails?> GetDetailsAsync(string slug, CancellationToken ct = default);
    Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default);
    Task<HomeModel> GetHomeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GameCard>> GetSimilarAsync(string slug, int count, CancellationToken ct = default);

    /// <summary>Drops cached catalogue data after a write; a no-op for the uncached catalogue.</summary>
    ValueTask InvalidateAsync(CancellationToken ct = default);
}

public interface ISimilarityStrategy
{
    double Score(SimilarityCandidate source, SimilarityCandidate other);
}
