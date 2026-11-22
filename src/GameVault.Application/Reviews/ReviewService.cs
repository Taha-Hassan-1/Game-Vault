using GameVault.Application.Common;
using GameVault.Application.Events;
using GameVault.Domain.Entities;

namespace GameVault.Application.Reviews;

public sealed class ReviewService
{
    public const int MaxBodyLength = 4000;
    public const int MaxReasonLength = 200;

    private readonly IReviewRepository _reviews;
    private readonly IDomainEventDispatcher _events;
    private readonly TimeProvider _time;

    public ReviewService(IReviewRepository reviews, IDomainEventDispatcher events, TimeProvider time)
    {
        _reviews = reviews;
        _events = events;
        _time = time;
    }

    public async Task<MyReview?> GetMineAsync(string userId, string slug, CancellationToken ct = default)
    {
        if (await _reviews.GetGameIdAsync(slug.ToLowerInvariant(), ct) is not { } gameId) return null;
        var r = await _reviews.GetByUserAsync(gameId, userId, ct);
        return r is null ? null : new MyReview(r.Id, r.Stars, r.Body, r.IsHidden);
    }

    /// <summary>Creates the member's review or edits it: one per member per game.</summary>
    public async Task<OpResult> SaveAsync(string userId, string slug, int stars, string? body, CancellationToken ct = default)
    {
        if (stars is < 1 or > 5) return OpResult.Fail("Choose a rating from 1 to 5 stars.");
        body = (body ?? "").Trim();
        if (body.Length > MaxBodyLength) return OpResult.Fail($"A review can be at most {MaxBodyLength} characters.");
        if (await _reviews.GetGameIdAsync(slug.ToLowerInvariant(), ct) is not { } gameId) return OpResult.Fail("That game does not exist.");

        var now = _time.GetUtcNow().UtcDateTime;
        var review = await _reviews.GetByUserAsync(gameId, userId, ct);
        if (review is null)
        {
            review = new Review { GameId = gameId, UserId = userId, CreatedAt = now };
            review.Write(stars, body);
            _reviews.Add(review);
        }
        else
        {
            review.Write(stars, body);
            review.EditedAt = now;
        }

        await _reviews.SaveAsync(ct);
        await _events.DispatchAsync(new ReviewChanged(gameId), ct);
        return OpResult.Success;
    }

    /// <summary>A member may remove their own review outright; only moderators hide other people's.</summary>
    public async Task<OpResult> DeleteMineAsync(string userId, string slug, CancellationToken ct = default)
    {
        if (await _reviews.GetGameIdAsync(slug.ToLowerInvariant(), ct) is not { } gameId) return OpResult.Fail("That game does not exist.");
        var review = await _reviews.GetByUserAsync(gameId, userId, ct);
        if (review is null) return OpResult.Fail("You have not reviewed this game.");

        _reviews.Remove(review);
        await _reviews.SaveAsync(ct);
        await _events.DispatchAsync(new ReviewChanged(gameId), ct);
        return OpResult.Success;
    }

    public async Task<OpResult> ReportAsync(string userId, int reviewId, string? reason, CancellationToken ct = default)
    {
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) reason = "No reason given";
        if (reason.Length > MaxReasonLength) reason = reason[..MaxReasonLength];

        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return OpResult.Fail("That review does not exist.");
        if (review.UserId == userId) return OpResult.Fail("You cannot report your own review.");
        return await _reviews.AddReportAsync(reviewId, userId, reason, ct)
            ? OpResult.Success
            : OpResult.Fail("You already reported this review.");
    }
}

public sealed class ModerationService
{
    private readonly IReviewRepository _reviews;
    private readonly IDomainEventDispatcher _events;

    public ModerationService(IReviewRepository reviews, IDomainEventDispatcher events)
    {
        _reviews = reviews;
        _events = events;
    }

    public Task<IReadOnlyList<ModerationItem>> GetQueueAsync(CancellationToken ct = default) => _reviews.GetModerationQueueAsync(ct);

    /// <summary>Hiding resolves the reports on that review; the review itself is kept so it can be restored.</summary>
    public async Task<OpResult> SetHiddenAsync(int reviewId, bool hidden, CancellationToken ct = default)
    {
        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return OpResult.Fail("That review does not exist.");

        review.IsHidden = hidden;
        await _reviews.SaveAsync(ct);
        if (hidden) await _reviews.ClearReportsAsync(reviewId, ct);
        await _events.DispatchAsync(new ReviewChanged(review.GameId), ct);
        return OpResult.Success;
    }

    public async Task<OpResult> DismissReportsAsync(int reviewId, CancellationToken ct = default)
    {
        if (await _reviews.GetByIdAsync(reviewId, ct) is null) return OpResult.Fail("That review does not exist.");
        await _reviews.ClearReportsAsync(reviewId, ct);
        return OpResult.Success;
    }
}

/// <summary>Observer: keeps Game.RatingAverage and RatingCount current so list pages never aggregate reviews.</summary>
public sealed class RatingRecalculator : IDomainEventHandler<ReviewChanged>
{
    private readonly IReviewRepository _reviews;
    private readonly Catalogue.IGameCatalog _catalog;

    public RatingRecalculator(IReviewRepository reviews, Catalogue.IGameCatalog catalog)
    {
        _reviews = reviews;
        _catalog = catalog;
    }

    public async Task HandleAsync(ReviewChanged e, CancellationToken ct = default)
    {
        var summary = await _reviews.GetRatingSummaryAsync(e.GameId, ct);
        await _reviews.UpdateGameRatingAsync(e.GameId, summary, ct);
        await _catalog.InvalidateAsync(ct);
    }
}
