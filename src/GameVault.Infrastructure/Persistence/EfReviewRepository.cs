using GameVault.Application.Reviews;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfReviewRepository : IReviewRepository
{
    private const int QueueLimit = 200;
    private readonly GameVaultDbContext _db;
    public EfReviewRepository(GameVaultDbContext db) => _db = db;

    public Task<int?> GetGameIdAsync(string slug, CancellationToken ct = default)
        => _db.Games.AsNoTracking().Where(g => g.Slug == slug).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);

    public Task<Review?> GetByUserAsync(int gameId, string userId, CancellationToken ct = default)
        => _db.Reviews.FirstOrDefaultAsync(r => r.GameId == gameId && r.UserId == userId, ct);

    public Task<Review?> GetByIdAsync(int reviewId, CancellationToken ct = default)
        => _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);

    public void Add(Review review) => _db.Reviews.Add(review);
    public void Remove(Review review) => _db.Reviews.Remove(review);
    public Task SaveAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<RatingSummary> GetRatingSummaryAsync(int gameId, CancellationToken ct = default)
    {
        var visible = _db.Reviews.AsNoTracking().Where(r => r.GameId == gameId && !r.IsHidden);
        var count = await visible.CountAsync(ct);
        return count == 0 ? new RatingSummary(0, 0) : new RatingSummary(count, await visible.AverageAsync(r => (double)r.Stars, ct));
    }

    public Task UpdateGameRatingAsync(int gameId, RatingSummary summary, CancellationToken ct = default)
    {
        var average = summary.Count == 0 ? 0 : Math.Round(summary.Average, 2);
        return _db.Games.Where(g => g.Id == gameId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RatingCount, summary.Count).SetProperty(g => g.RatingAverage, average), ct);
    }

    public async Task<bool> AddReportAsync(int reviewId, string userId, string reason, CancellationToken ct = default)
    {
        if (await _db.ReviewReports.AnyAsync(x => x.ReviewId == reviewId && x.UserId == userId, ct)) return false;
        _db.ReviewReports.Add(new ReviewReport { ReviewId = reviewId, UserId = userId, Reason = reason });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // A double submit raced past the check; the composite key makes it a duplicate.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task ClearReportsAsync(int reviewId, CancellationToken ct = default)
        => _db.ReviewReports.Where(x => x.ReviewId == reviewId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<ModerationItem>> GetModerationQueueAsync(CancellationToken ct = default)
    {
        var rows = await (
            from r in _db.Reviews.AsNoTracking()
            where r.IsHidden || _db.ReviewReports.Any(x => x.ReviewId == r.Id)
            join g in _db.Games on r.GameId equals g.Id
            join u in _db.Users on r.UserId equals u.Id
            orderby r.CreatedAt descending
            select new
            {
                r.Id, g.Slug, g.Title, u.DisplayName, r.Stars, r.Body, r.IsHidden, r.CreatedAt,
                Reports = _db.ReviewReports.Where(x => x.ReviewId == r.Id).Select(x => x.Reason).ToList()
            }).Take(QueueLimit).ToListAsync(ct);

        // Reported reviews first: they need a decision, hidden ones are already handled.
        return rows
            .OrderByDescending(x => x.Reports.Count).ThenByDescending(x => x.CreatedAt)
            .Select(x => new ModerationItem(x.Id, x.Slug, x.Title, x.DisplayName, x.Stars, x.Body, x.IsHidden, x.Reports, x.CreatedAt))
            .ToList();
    }
}
