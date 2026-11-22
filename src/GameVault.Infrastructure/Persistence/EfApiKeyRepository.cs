using GameVault.Application.Studio;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfApiKeyRepository : IApiKeyRepository
{
    private readonly GameVaultDbContext _db;
    public EfApiKeyRepository(GameVaultDbContext db) => _db = db;

    public async Task AddAsync(ApiKey key, CancellationToken ct = default)
    {
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync(ct);
    }

    public Task<int?> FindActiveGameIdAsync(string keyHash, CancellationToken ct = default)
        => _db.ApiKeys.AsNoTracking().Where(k => k.KeyHash == keyHash && k.RevokedAt == null)
            .Select(k => (int?)k.GameId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ApiKeyRow>> ListAsync(CancellationToken ct = default)
        => await _db.ApiKeys.AsNoTracking().OrderBy(k => k.RevokedAt != null).ThenByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyRow(k.Id, k.GameId, k.Game!.Title, k.KeyPrefix, k.CreatedAt, k.RevokedAt))
            .ToListAsync(ct);

    public async Task<bool> RevokeAsync(int id, DateTime now, CancellationToken ct = default)
        => await _db.ApiKeys.Where(k => k.Id == id && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, (DateTime?)now), ct) > 0;
}
