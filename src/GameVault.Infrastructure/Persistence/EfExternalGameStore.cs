using GameVault.Application.Metadata;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfExternalGameStore : IExternalGameStore
{
    private const string SyncPageKey = "rawg.sync.page";

    private readonly GameVaultDbContext _db;
    private readonly TimeProvider _time;

    public EfExternalGameStore(GameVaultDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<int> UpsertAsync(IReadOnlyList<ExternalGame> games, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var created = 0;
        var genres = new Dictionary<string, Genre>();
        var platforms = new Dictionary<string, Platform>();
        var tags = new Dictionary<string, Tag>();

        foreach (var ext in games)
        {
            var game = await WithRelations()
                .FirstOrDefaultAsync(g => g.Source == GameSource.Rawg && g.ExternalId == ext.ExternalId, ct);

            // Seeded sample games have no ExternalId yet; adopt them instead of importing a duplicate.
            game ??= await WithRelations()
                .FirstOrDefaultAsync(g => g.Source == GameSource.Rawg && g.ExternalId == null && g.Slug == ext.Slug, ct);

            if (game is null)
            {
                game = new Game { Source = GameSource.Rawg, Slug = await UniqueSlugAsync(ext, ct) };
                _db.Games.Add(game);
                created++;
            }

            game.ExternalId = ext.ExternalId;
            game.Title = ext.Title;
            game.ReleaseDate = ext.ReleaseDate ?? game.ReleaseDate;
            game.CoverUrl = ext.CoverUrl ?? game.CoverUrl;
            game.ExternalFetchedAt = now;

            if (ext.IsDetailed)
            {
                game.Description = ext.Description ?? "";
                game.DetailsFetchedAt = now;
                game.Screenshots.Clear();
                foreach (var url in ext.Screenshots.Take(Game.MaxScreenshots)) game.Screenshots.Add(new Screenshot { Url = url });
            }

            game.Genres = await ResolveAsync(_db.Genres, ext.Genres, genres, () => new Genre(), ct);
            game.Platforms = await ResolveAsync(_db.Platforms, ext.Platforms, platforms, () => new Platform(), ct);
            game.Tags = await ResolveAsync(_db.Tags, ext.Tags, tags, () => new Tag(), ct);
        }

        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<string?> GetExternalIdNeedingDetailsAsync(string slug, DateTime staleBefore, CancellationToken ct = default)
        => await _db.Games.AsNoTracking()
            .Where(g => g.Slug == slug && g.Source == GameSource.Rawg && g.ExternalId != null
                        && (g.DetailsFetchedAt == null || g.DetailsFetchedAt < staleBefore))
            .Select(g => g.ExternalId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> GetStaleExternalIdsAsync(DateTime staleBefore, int take, CancellationToken ct = default)
        => await _db.Games.AsNoTracking()
            .Where(g => g.Source == GameSource.Rawg && g.ExternalId != null
                        && (g.ExternalFetchedAt == null || g.ExternalFetchedAt < staleBefore))
            .OrderBy(g => g.ExternalFetchedAt)
            .Select(g => g.ExternalId!).Take(take).ToListAsync(ct);

    public async Task<int> GetSyncPageAsync(CancellationToken ct = default)
        => (await _db.SyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SyncPageKey, ct))?.Value ?? 1;

    public async Task SetSyncPageAsync(int page, CancellationToken ct = default)
    {
        var state = await _db.SyncStates.FirstOrDefaultAsync(s => s.Key == SyncPageKey, ct);
        if (state is null) _db.SyncStates.Add(new SyncState { Key = SyncPageKey, Value = page });
        else state.Value = page;
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable<Game> WithRelations() => _db.Games
        .Include(g => g.Genres).Include(g => g.Platforms).Include(g => g.Tags).Include(g => g.Screenshots)
        .AsSplitQuery();

    private async Task<string> UniqueSlugAsync(ExternalGame ext, CancellationToken ct)
    {
        var taken = await _db.Games.AnyAsync(g => g.Slug == ext.Slug, ct)
                    || _db.Games.Local.Any(g => g.Slug == ext.Slug);
        return taken ? $"{ext.Slug}-{ext.ExternalId}" : ext.Slug;
    }

    private static async Task<List<T>> ResolveAsync<T>(
        DbSet<T> set, IReadOnlyList<ExternalTaxon> wanted, Dictionary<string, T> cache, Func<T> create, CancellationToken ct)
        where T : Taxon
    {
        var result = new List<T>();
        foreach (var t in wanted)
        {
            if (!cache.TryGetValue(t.Slug, out var entity))
            {
                entity = await set.FirstOrDefaultAsync(x => x.Slug == t.Slug, ct);
                if (entity is null)
                {
                    entity = create();
                    entity.Name = t.Name;
                    entity.Slug = t.Slug;
                    set.Add(entity);
                }
                cache[t.Slug] = entity;
            }
            result.Add(entity);
        }
        return result;
    }
}

/// <summary>Singleton so the HTTP handler can count requests without holding a DbContext for its whole pooled lifetime.</summary>
public sealed class EfApiUsageStore : IApiUsageStore
{
    private readonly IServiceScopeFactory _scopes;
    public EfApiUsageStore(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<int> GetMonthTotalAsync(string provider, DateOnly anyDayInMonth, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var start = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        return await db.ApiUsage.AsNoTracking()
            .Where(u => u.Provider == provider && u.Date >= start && u.Date < start.AddMonths(1))
            .SumAsync(u => (int?)u.Count, ct) ?? 0;
    }

    public async Task IncrementAsync(string provider, DateOnly day, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();

        if (await Bump(db, provider, day, ct) > 0) return;
        try
        {
            db.ApiUsage.Add(new ApiUsage { Provider = provider, Date = day, Count = 1 });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request inserted today's row first; add to it instead.
            db.ChangeTracker.Clear();
            await Bump(db, provider, day, ct);
        }
    }

    private static Task<int> Bump(GameVaultDbContext db, string provider, DateOnly day, CancellationToken ct)
        => db.ApiUsage.Where(u => u.Provider == provider && u.Date == day)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Count, u => u.Count + 1), ct);
}
