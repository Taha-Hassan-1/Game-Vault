using GameVault.Application.Catalogue;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfGameRepository : IGameRepository
{
    private readonly GameVaultDbContext _db;
    public EfGameRepository(GameVaultDbContext db) => _db = db;

    public async Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery q, CancellationToken ct = default)
    {
        IQueryable<Game> query = _db.Games.AsNoTracking();

        if (q.Search is not null)
        {
            var term = q.Search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            query = query.Where(g => EF.Functions.Like(g.Title, "%" + term + "%")
                                   || EF.Functions.Like(g.Description, "%" + term + "%"));
        }
        if (q.Genre is not null) query = query.Where(g => g.Genres.Any(x => x.Slug == q.Genre));
        if (q.Platform is not null) query = query.Where(g => g.Platforms.Any(x => x.Slug == q.Platform));
        if (q.Tag is not null) query = query.Where(g => g.Tags.Any(x => x.Slug == q.Tag));

        var total = await query.CountAsync(ct);

        query = q.Sort switch
        {
            GameSort.TopRated => query.OrderByDescending(g => g.RatingAverage).ThenByDescending(g => g.RatingCount).ThenBy(g => g.Title),
            GameSort.Title => query.OrderBy(g => g.Title),
            GameSort.Released => query.OrderByDescending(g => g.ReleaseDate).ThenBy(g => g.Title),
            _ => query.OrderByDescending(g => g.CreatedAt).ThenBy(g => g.Title)
        };

        var items = await Cards(query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)).ToListAsync(ct);
        return new PagedResult<GameCard>(items, q.Page, q.PageSize, total);
    }

    public async Task<GameDetails?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var row = await _db.Games.AsNoTracking().AsSplitQuery()
            .Where(g => g.Slug == slug)
            .Select(g => new
            {
                g.Id, g.Slug, g.Title, g.Description, g.CoverUrl, g.ReleaseDate, g.RatingAverage, g.RatingCount,
                g.IsStudioGame, g.Source,
                Genres = g.Genres.OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToList(),
                Platforms = g.Platforms.OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToList(),
                Tags = g.Tags.OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToList(),
                Screenshots = g.Screenshots.Select(x => x.Url).ToList()
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var reviews = await (
            from r in _db.Reviews.AsNoTracking()
            where r.GameId == row.Id && !r.IsHidden
            join u in _db.Users on r.UserId equals u.Id
            orderby r.CreatedAt descending
            select new ReviewItem(r.Id, u.DisplayName, r.Stars, r.Body, r.CreatedAt, r.EditedAt))
            .Take(50).ToListAsync(ct);

        return new GameDetails(row.Id, row.Slug, row.Title, row.Description, row.CoverUrl, row.ReleaseDate,
            row.RatingAverage, row.RatingCount, row.IsStudioGame, row.Source == GameSource.Rawg,
            row.Genres, row.Platforms, row.Tags, row.Screenshots, reviews);
    }

    public async Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default)
    {
        var genres = await _db.Genres.AsNoTracking().OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToListAsync(ct);
        var platforms = await _db.Platforms.AsNoTracking().OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToListAsync(ct);
        var tags = await _db.Tags.AsNoTracking().OrderBy(x => x.Name).Select(x => new TaxonItem(x.Name, x.Slug)).ToListAsync(ct);
        return new CatalogueFacets(genres, platforms, tags);
    }

    public async Task<HomeModel> GetHomeAsync(int perSection, CancellationToken ct = default)
    {
        var games = _db.Games.AsNoTracking();
        var featured = await Cards(games.Where(g => g.IsStudioGame).OrderByDescending(g => g.CreatedAt).Take(perSection)).ToListAsync(ct);
        var top = await Cards(games.Where(g => g.RatingCount > 0)
            .OrderByDescending(g => g.RatingAverage).ThenByDescending(g => g.RatingCount).Take(perSection)).ToListAsync(ct);
        var recent = await Cards(games.OrderByDescending(g => g.CreatedAt).ThenBy(g => g.Title).Take(perSection)).ToListAsync(ct);
        return new HomeModel(featured, top, recent);
    }

    public async Task<SimilarityCandidate?> GetCandidateAsync(string slug, CancellationToken ct = default)
        => (await Candidates(_db.Games.AsNoTracking().Where(g => g.Slug == slug), 1, ct)).FirstOrDefault();

    public Task<IReadOnlyList<SimilarityCandidate>> GetCandidatePoolAsync(string excludeSlug, int poolSize, CancellationToken ct = default)
        => Candidates(_db.Games.AsNoTracking().Where(g => g.Slug != excludeSlug)
            .OrderByDescending(g => g.RatingAverage).ThenBy(g => g.Id), poolSize, ct);

    private static IQueryable<GameCard> Cards(IQueryable<Game> q) => q.Select(g => new GameCard(
        g.Id, g.Slug, g.Title, g.CoverUrl, g.ReleaseDate, g.RatingAverage, g.RatingCount, g.IsStudioGame,
        g.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList()));

    private static async Task<IReadOnlyList<SimilarityCandidate>> Candidates(IQueryable<Game> q, int take, CancellationToken ct)
    {
        var rows = await q.Take(take).Select(g => new
        {
            Card = new GameCard(g.Id, g.Slug, g.Title, g.CoverUrl, g.ReleaseDate, g.RatingAverage, g.RatingCount,
                g.IsStudioGame, g.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList()),
            GenreSlugs = g.Genres.Select(x => x.Slug).ToList(),
            TagSlugs = g.Tags.Select(x => x.Slug).ToList()
        }).ToListAsync(ct);
        return rows.Select(r => new SimilarityCandidate(r.Card, r.GenreSlugs.ToHashSet(), r.TagSlugs.ToHashSet())).ToList();
    }
}
