using GameVault.Application.Catalogue;
using GameVault.Application.Lists;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfListRepository : IListRepository
{
    private readonly GameVaultDbContext _db;
    public EfListRepository(GameVaultDbContext db) => _db = db;

    public Task<int?> GetGameIdAsync(string slug, CancellationToken ct = default)
        => _db.Games.AsNoTracking().Where(g => g.Slug == slug).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default)
        => _db.ListEntries.AnyAsync(e => e.UserId == userId && e.GameId == gameId && e.Kind == kind, ct);

    public async Task AddAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default)
    {
        _db.ListEntries.Add(new ListEntry { UserId = userId, GameId = gameId, Kind = kind });
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); } // already added by a parallel request: same end state
    }

    public Task RemoveAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default)
        => _db.ListEntries.Where(e => e.UserId == userId && e.GameId == gameId && e.Kind == kind).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<GameCard>> GetCardsAsync(string userId, ListKind kind, CancellationToken ct = default)
        => await _db.ListEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Kind == kind)
            .OrderByDescending(e => e.AddedAt)
            .Select(e => new GameCard(
                e.Game!.Id, e.Game.Slug, e.Game.Title, e.Game.CoverUrl, e.Game.ReleaseDate, e.Game.RatingAverage,
                e.Game.RatingCount, e.Game.IsStudioGame, e.Game.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList()))
            .ToListAsync(ct);

    public async Task<IReadOnlySet<ListKind>> GetKindsAsync(string userId, int gameId, CancellationToken ct = default)
        => (await _db.ListEntries.AsNoTracking().Where(e => e.UserId == userId && e.GameId == gameId).Select(e => e.Kind).ToListAsync(ct)).ToHashSet();
}
