namespace GameVault.Application.Catalogue;

public enum GameSort { Newest, TopRated, Title, Released }

public sealed record CatalogueQuery(
    string? Search = null,
    string? Genre = null,
    string? Platform = null,
    string? Tag = null,
    GameSort Sort = GameSort.Newest,
    int Page = 1,
    int PageSize = CatalogueQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 24;

    public CatalogueQuery Normalized() => this with
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Genre = Blank(Genre),
        Platform = Blank(Platform),
        Tag = Blank(Tag),
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize)
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public sealed record GameCard(
    int Id, string Slug, string Title, string? CoverUrl, DateOnly? ReleaseDate,
    double RatingAverage, int RatingCount, bool IsStudioGame, IReadOnlyList<string> Genres);

public sealed record TaxonItem(string Name, string Slug);

public sealed record ReviewItem(int Id, string AuthorName, int Stars, string Body, DateTime CreatedAt, DateTime? EditedAt);

public sealed record GameDetails(
    int Id, string Slug, string Title, string Description, string? CoverUrl, DateOnly? ReleaseDate,
    double RatingAverage, int RatingCount, bool IsStudioGame, bool IsRawg,
    IReadOnlyList<TaxonItem> Genres, IReadOnlyList<TaxonItem> Platforms, IReadOnlyList<TaxonItem> Tags,
    IReadOnlyList<string> Screenshots, IReadOnlyList<ReviewItem> Reviews);

public sealed record CatalogueFacets(
    IReadOnlyList<TaxonItem> Genres, IReadOnlyList<TaxonItem> Platforms, IReadOnlyList<TaxonItem> Tags);

public sealed record HomeModel(
    IReadOnlyList<GameCard> Featured, IReadOnlyList<GameCard> TopRated, IReadOnlyList<GameCard> Recent);

/// <summary>A game plus the taxonomy slugs the similarity strategy compares.</summary>
public sealed record SimilarityCandidate(GameCard Card, IReadOnlySet<string> Genres, IReadOnlySet<string> Tags);
