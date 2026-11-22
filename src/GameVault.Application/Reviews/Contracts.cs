using GameVault.Domain.Entities;

namespace GameVault.Application.Reviews;

public sealed record MyReview(int Id, int Stars, string Body, bool IsHidden);

public sealed record RatingSummary(int Count, double Average);

public sealed record ModerationItem(
    int ReviewId, string GameSlug, string GameTitle, string AuthorName, int Stars, string Body,
    bool IsHidden, IReadOnlyList<string> Reports, DateTime CreatedAt);

public interface IReviewRepository
{
    Task<int?> GetGameIdAsync(string slug, CancellationToken ct = default);
    Task<Review?> GetByUserAsync(int gameId, string userId, CancellationToken ct = default);
    Task<Review?> GetByIdAsync(int reviewId, CancellationToken ct = default);
    void Add(Review review);
    void Remove(Review review);
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Counts and averages only reviews that are not hidden.</summary>
    Task<RatingSummary> GetRatingSummaryAsync(int gameId, CancellationToken ct = default);
    Task UpdateGameRatingAsync(int gameId, RatingSummary summary, CancellationToken ct = default);

    /// <summary>False when this member already reported the review.</summary>
    Task<bool> AddReportAsync(int reviewId, string userId, string reason, CancellationToken ct = default);
    Task ClearReportsAsync(int reviewId, CancellationToken ct = default);
    Task<IReadOnlyList<ModerationItem>> GetModerationQueueAsync(CancellationToken ct = default);
}
