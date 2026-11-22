using GameVault.Domain;

namespace GameVault.Application.Studio;

public sealed record StudioGameRow(int Id, string Slug, string Title, double RatingAverage, int RatingCount, int ScreenshotCount);

public sealed record ScreenshotItem(int Id, string Url);

public sealed record StudioGameDraft(
    string Title, string Description, DateOnly? ReleaseDate, string? CoverUrl,
    IReadOnlyList<string> GenreSlugs, IReadOnlyList<string> PlatformSlugs, IReadOnlyList<string> TagSlugs);

public sealed record StudioGameEdit(int Id, string Slug, StudioGameDraft Draft, IReadOnlyList<ScreenshotItem> Screenshots);

public interface IStudioGameRepository
{
    Task<IReadOnlyList<StudioGameRow>> ListAsync(CancellationToken ct = default);
    Task<StudioGameEdit?> GetAsync(int id, CancellationToken ct = default);
    Task<bool> IsStudioGameAsync(int id, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<int> CreateAsync(string slug, StudioGameDraft draft, CancellationToken ct = default);

    /// <summary>False when the game does not exist or is not a studio game.</summary>
    Task<bool> UpdateAsync(int id, StudioGameDraft draft, CancellationToken ct = default);

    Task<int> CountScreenshotsAsync(int gameId, CancellationToken ct = default);
    Task AddScreenshotAsync(int gameId, string url, CancellationToken ct = default);

    /// <summary>Returns the removed screenshot's URL so its file can be deleted, or null when it was not found.</summary>
    Task<string?> RemoveScreenshotAsync(int gameId, int screenshotId, CancellationToken ct = default);

    /// <summary>Returns the previous cover URL (possibly null) so an uploaded file can be deleted.</summary>
    Task<string?> SetCoverAsync(int gameId, string url, CancellationToken ct = default);
}

public interface IImageStore
{
    /// <summary>Stores the bytes under a generated name and returns the public URL. The caller has already checked the content.</summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default);

    /// <summary>Deletes a file this store created; anything else (an external URL, a path trick) is ignored.</summary>
    Task DeleteAsync(string url, CancellationToken ct = default);
}

public sealed record StudioGameSummary(int Id, string Slug, string Title, double RatingAverage, int RatingCount, int NewFeedback, int OpenSuggestions);

public sealed record VersionRow(string GameTitle, string AppVersion, int Bugs, int Ideas, int Praise);

public sealed record StudioDashboard(IReadOnlyList<StudioGameSummary> Games, IReadOnlyList<Feedback.FeedbackItem> Newest, IReadOnlyList<VersionRow> ByVersion);
