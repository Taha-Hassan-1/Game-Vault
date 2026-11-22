using GameVault.Domain;

namespace GameVault.Application.Metadata;

public sealed record ExternalTaxon(string Name, string Slug);

/// <summary>A game as a metadata provider describes it, free of any provider JSON shape.</summary>
public sealed record ExternalGame(
    string ExternalId,
    string Slug,
    string Title,
    string? Description,
    DateOnly? ReleaseDate,
    string? CoverUrl,
    IReadOnlyList<ExternalTaxon> Genres,
    IReadOnlyList<ExternalTaxon> Platforms,
    IReadOnlyList<ExternalTaxon> Tags,
    IReadOnlyList<string> Screenshots,
    /// <summary>True when description and screenshots were fetched (the details call), false for list rows.</summary>
    bool IsDetailed);

public interface IGameMetadataProvider
{
    GameSource Source { get; }

    /// <summary>Basic rows (title, cover, taxonomy). A null query lists popular games.</summary>
    Task<IReadOnlyList<ExternalGame>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Full details, including description and screenshots.</summary>
    Task<ExternalGame?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
}

/// <summary>Factory: picks a provider by source so IGDB can be added without touching callers.</summary>
public interface IGameMetadataProviderFactory
{
    IGameMetadataProvider? Get(GameSource source);
}

public sealed class GameMetadataProviderFactory : IGameMetadataProviderFactory
{
    private readonly IReadOnlyList<IGameMetadataProvider> _providers;
    public GameMetadataProviderFactory(IEnumerable<IGameMetadataProvider> providers) => _providers = providers.ToList();
    public IGameMetadataProvider? Get(GameSource source) => _providers.FirstOrDefault(p => p.Source == source);
}

/// <summary>Where imported provider rows are persisted; only Infrastructure knows about EF.</summary>
public interface IExternalGameStore
{
    /// <summary>Inserts or updates games; returns how many were new.</summary>
    Task<int> UpsertAsync(IReadOnlyList<ExternalGame> games, CancellationToken ct = default);

    /// <summary>The provider id when this game's details are missing or older than <paramref name="staleBefore"/>; otherwise null.</summary>
    Task<string?> GetExternalIdNeedingDetailsAsync(string slug, DateTime staleBefore, CancellationToken ct = default);

    /// <summary>Provider ids of the stalest rows, oldest first.</summary>
    Task<IReadOnlyList<string>> GetStaleExternalIdsAsync(DateTime staleBefore, int take, CancellationToken ct = default);

    Task<int> GetSyncPageAsync(CancellationToken ct = default);
    Task SetSyncPageAsync(int page, CancellationToken ct = default);
}
