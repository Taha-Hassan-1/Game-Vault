using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfStudioGameRepository : IStudioGameRepository
{
    private readonly GameVaultDbContext _db;
    public EfStudioGameRepository(GameVaultDbContext db) => _db = db;

    public async Task<IReadOnlyList<StudioGameRow>> ListAsync(CancellationToken ct = default)
        => await _db.Games.AsNoTracking().Where(g => g.IsStudioGame).OrderBy(g => g.Title)
            .Select(g => new StudioGameRow(g.Id, g.Slug, g.Title, g.RatingAverage, g.RatingCount, g.Screenshots.Count))
            .ToListAsync(ct);

    public async Task<StudioGameEdit?> GetAsync(int id, CancellationToken ct = default)
    {
        var g = await _db.Games.AsNoTracking().AsSplitQuery()
            .Where(x => x.Id == id && x.IsStudioGame)
            .Select(x => new
            {
                x.Id, x.Slug, x.Title, x.Description, x.ReleaseDate, x.CoverUrl,
                Genres = x.Genres.Select(t => t.Slug).ToList(),
                Platforms = x.Platforms.Select(t => t.Slug).ToList(),
                Tags = x.Tags.Select(t => t.Slug).ToList(),
                Shots = x.Screenshots.OrderBy(s => s.Id).Select(s => new ScreenshotItem(s.Id, s.Url)).ToList()
            }).FirstOrDefaultAsync(ct);

        return g is null ? null : new StudioGameEdit(g.Id, g.Slug,
            new StudioGameDraft(g.Title, g.Description, g.ReleaseDate, g.CoverUrl, g.Genres, g.Platforms, g.Tags), g.Shots);
    }

    public Task<bool> IsStudioGameAsync(int id, CancellationToken ct = default)
        => _db.Games.AsNoTracking().AnyAsync(g => g.Id == id && g.IsStudioGame, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
        => _db.Games.AsNoTracking().AnyAsync(g => g.Slug == slug, ct);

    public async Task<int> CreateAsync(string slug, StudioGameDraft d, CancellationToken ct = default)
    {
        var game = new Game
        {
            Slug = slug, Source = GameSource.Studio, IsStudioGame = true, CreatedAt = DateTime.UtcNow
        };
        Apply(game, d);
        await AttachTaxonomyAsync(game, d, ct);
        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);
        return game.Id;
    }

    public async Task<bool> UpdateAsync(int id, StudioGameDraft d, CancellationToken ct = default)
    {
        var game = await _db.Games.Include(g => g.Genres).Include(g => g.Platforms).Include(g => g.Tags)
            .AsSplitQuery().FirstOrDefaultAsync(g => g.Id == id && g.IsStudioGame, ct);
        if (game is null) return false;

        Apply(game, d); // the slug is deliberately not touched
        await AttachTaxonomyAsync(game, d, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> CountScreenshotsAsync(int gameId, CancellationToken ct = default)
        => _db.Screenshots.CountAsync(s => s.GameId == gameId, ct);

    public async Task AddScreenshotAsync(int gameId, string url, CancellationToken ct = default)
    {
        _db.Screenshots.Add(new Screenshot { GameId = gameId, Url = url });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> RemoveScreenshotAsync(int gameId, int screenshotId, CancellationToken ct = default)
    {
        var shot = await _db.Screenshots.FirstOrDefaultAsync(s => s.Id == screenshotId && s.GameId == gameId, ct);
        if (shot is null) return null;
        _db.Screenshots.Remove(shot);
        await _db.SaveChangesAsync(ct);
        return shot.Url;
    }

    public async Task<string?> SetCoverAsync(int gameId, string url, CancellationToken ct = default)
    {
        var game = await _db.Games.FirstAsync(g => g.Id == gameId, ct);
        var previous = game.CoverUrl;
        game.CoverUrl = url;
        await _db.SaveChangesAsync(ct);
        return previous;
    }

    private static void Apply(Game game, StudioGameDraft d)
    {
        game.Title = d.Title;
        game.Description = d.Description;
        game.ReleaseDate = d.ReleaseDate;
        game.CoverUrl = d.CoverUrl;
    }

    // Unknown slugs are ignored rather than created: the studio picks from the existing taxonomy.
    private async Task AttachTaxonomyAsync(Game game, StudioGameDraft d, CancellationToken ct)
    {
        game.Genres = await _db.Genres.Where(x => d.GenreSlugs.Contains(x.Slug)).ToListAsync(ct);
        game.Platforms = await _db.Platforms.Where(x => d.PlatformSlugs.Contains(x.Slug)).ToListAsync(ct);
        game.Tags = await _db.Tags.Where(x => d.TagSlugs.Contains(x.Slug)).ToListAsync(ct);
    }
}
