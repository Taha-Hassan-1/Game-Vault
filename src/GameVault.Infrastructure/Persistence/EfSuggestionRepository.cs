using GameVault.Application.Suggestions;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfSuggestionRepository : ISuggestionRepository
{
    private readonly GameVaultDbContext _db;
    public EfSuggestionRepository(GameVaultDbContext db) => _db = db;

    public Task<SuggestionGame?> GetGameAsync(string slug, CancellationToken ct = default)
        => _db.Games.AsNoTracking().Where(g => g.Slug == slug)
            .Select(g => new SuggestionGame(g.Id, g.Slug, g.Title, g.IsStudioGame)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SuggestionItem>> ListAsync(int gameId, string? userId, SuggestionStatus? status, CancellationToken ct = default)
    {
        var q = _db.Suggestions.AsNoTracking().Where(s => s.GameId == gameId);
        if (status is { } st) q = q.Where(s => s.Status == st);

        return await (
            from s in q
            join u in _db.Users on s.AuthorId equals u.Id
            orderby s.VoteCount descending, s.CreatedAt descending
            select new SuggestionItem(s.Id, s.Title, s.Body, s.Status, s.VoteCount, u.DisplayName, s.CreatedAt,
                userId != null && s.Votes.Any(v => v.UserId == userId)))
            .Take(200).ToListAsync(ct);
    }

    public Task<Suggestion?> GetAsync(int id, CancellationToken ct = default)
        => _db.Suggestions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<int> AddAsync(Suggestion suggestion, CancellationToken ct = default)
    {
        _db.Suggestions.Add(suggestion);
        await _db.SaveChangesAsync(ct);
        return suggestion.Id;
    }

    public async Task<bool> SetVoteAsync(int suggestionId, string userId, bool present, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var changed = false;

        if (present)
        {
            if (!await _db.SuggestionVotes.AnyAsync(v => v.SuggestionId == suggestionId && v.UserId == userId, ct))
            {
                _db.SuggestionVotes.Add(new SuggestionVote { SuggestionId = suggestionId, UserId = userId });
                try
                {
                    await _db.SaveChangesAsync(ct);
                    changed = true;
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear(); // a parallel request added the same vote: the end state is what the caller wanted
                }
            }
        }
        else
        {
            changed = await _db.SuggestionVotes.Where(v => v.SuggestionId == suggestionId && v.UserId == userId).ExecuteDeleteAsync(ct) > 0;
        }

        if (changed)
        {
            // Recounted from the vote rows, not incremented, so the number cannot drift.
            await _db.Suggestions.Where(s => s.Id == suggestionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoteCount, x => _db.SuggestionVotes.Count(v => v.SuggestionId == x.Id)), ct);
        }

        await tx.CommitAsync(ct);
        return changed;
    }

    public async Task<bool> SetStatusAsync(int id, SuggestionStatus status, CancellationToken ct = default)
        => await _db.Suggestions.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), ct) > 0;
}
