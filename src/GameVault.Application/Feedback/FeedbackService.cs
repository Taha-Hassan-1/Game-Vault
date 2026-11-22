using System.Text;
using System.Text.RegularExpressions;
using GameVault.Application.Catalogue;
using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Domain.Entities;

namespace GameVault.Application.Feedback;

public sealed record FeedbackItem(
    int Id, int GameId, string GameTitle, FeedbackSource Source, FeedbackCategory Category, string Body,
    string? PlayerRef, string? AppVersion, FeedbackStatus Status, DateTime CreatedAt);

public sealed record FeedbackFilter(
    int? GameId = null, FeedbackCategory? Category = null, FeedbackStatus? Status = null, FeedbackSource? Source = null,
    int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 100;
    public FeedbackFilter Normalized() => this with { Page = Math.Max(1, Page), PageSize = Math.Clamp(PageSize, 1, MaxPageSize) };
}

public sealed record FeedbackSubmission(int GameId, FeedbackSource Source, string? Category, string? Body, string? PlayerRef, string? AppVersion);

public sealed record FeedbackResult(bool Ok, int Id, IReadOnlyDictionary<string, string[]> Errors)
{
    public static FeedbackResult Success(int id) => new(true, id, new Dictionary<string, string[]>());
    public static FeedbackResult Invalid(Dictionary<string, string[]> errors) => new(false, 0, errors);
}

public interface IFeedbackRepository
{
    Task<int> AddAsync(FeedbackMessage message, CancellationToken ct = default);
    Task<PagedResult<FeedbackItem>> ListAsync(FeedbackFilter filter, CancellationToken ct = default);
    Task<IReadOnlyList<FeedbackItem>> ExportAsync(FeedbackFilter filter, int limit, CancellationToken ct = default);
    Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default);
    Task<StudioDashboard> GetDashboardAsync(CancellationToken ct = default);
}

public sealed partial class FeedbackService
{
    public const int MaxBody = 2000;
    public const int MaxPlayerRef = 100;
    public const int MaxAppVersion = 32;
    public const int ExportLimit = 5000;

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z._+\-]*$")]
    private static partial Regex VersionPattern();

    private readonly IFeedbackRepository _feedback;
    private readonly IStudioGameRepository _games;
    private readonly TimeProvider _time;

    public FeedbackService(IFeedbackRepository feedback, IStudioGameRepository games, TimeProvider time)
    {
        _feedback = feedback;
        _games = games;
        _time = time;
    }

    /// <summary>Shared by the web form and the Unity API, so both enforce the same rules.</summary>
    public async Task<FeedbackResult> SubmitAsync(FeedbackSubmission s, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();

        var category = ParseCategory(s.Category);
        if (category is null) errors["category"] = new[] { "Use one of: Bug, Idea, Praise." };

        var body = (s.Body ?? "").Trim();
        if (body.Length == 0) errors["body"] = new[] { "Write a message." };
        else if (body.Length > MaxBody) errors["body"] = new[] { $"The message can be at most {MaxBody} characters." };

        var playerRef = string.IsNullOrWhiteSpace(s.PlayerRef) ? null : s.PlayerRef.Trim();
        if (playerRef is not null && (playerRef.Length > MaxPlayerRef || playerRef.Any(char.IsControl)))
            errors["playerRef"] = new[] { $"playerRef can be at most {MaxPlayerRef} characters, without control characters." };

        var version = string.IsNullOrWhiteSpace(s.AppVersion) ? null : s.AppVersion.Trim();
        if (version is not null && (version.Length > MaxAppVersion || !VersionPattern().IsMatch(version)))
            errors["appVersion"] = new[] { "appVersion can use letters, digits and . _ + - only, up to 32 characters." };

        if (errors.Count > 0) return FeedbackResult.Invalid(errors);
        if (!await _games.IsStudioGameAsync(s.GameId, ct))
            return FeedbackResult.Invalid(new() { ["game"] = new[] { "Feedback is only accepted for studio games." } });

        var id = await _feedback.AddAsync(new FeedbackMessage
        {
            GameId = s.GameId, Source = s.Source, Category = category!.Value, Body = body,
            PlayerRef = playerRef, AppVersion = version, CreatedAt = _time.GetUtcNow().UtcDateTime
        }, ct);
        return FeedbackResult.Success(id);
    }

    /// <summary>Names only: Enum.TryParse would also accept "1" or "Bug, Idea".</summary>
    public static FeedbackCategory? ParseCategory(string? text)
    {
        foreach (var name in Enum.GetNames<FeedbackCategory>())
            if (string.Equals(name, text?.Trim(), StringComparison.OrdinalIgnoreCase)) return Enum.Parse<FeedbackCategory>(name);
        return null;
    }

    public Task<PagedResult<FeedbackItem>> ListAsync(FeedbackFilter filter, CancellationToken ct = default)
        => _feedback.ListAsync(filter.Normalized(), ct);

    public Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)
        => Enum.IsDefined(status) ? _feedback.SetStatusAsync(id, status, ct) : Task.FromResult(false);

    public Task<StudioDashboard> GetDashboardAsync(CancellationToken ct = default) => _feedback.GetDashboardAsync(ct);

    public async Task<string> ExportCsvAsync(FeedbackFilter filter, CancellationToken ct = default)
    {
        var rows = await _feedback.ExportAsync(filter.Normalized(), ExportLimit, ct);
        var sb = new StringBuilder();
        sb.Append(Csv.Row("Id", "Game", "Source", "Category", "Status", "AppVersion", "PlayerRef", "CreatedUtc", "Body"));
        foreach (var r in rows)
            sb.Append(Csv.Row(r.Id.ToString(), r.GameTitle, r.Source.ToString(), r.Category.ToString(), r.Status.ToString(),
                r.AppVersion ?? "", r.PlayerRef ?? "", r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), r.Body));
        return sb.ToString();
    }
}

public static class Csv
{
    /// <summary>
    /// One RFC 4180 row. Cells that start with = + - @ (or a tab or carriage return) get a leading apostrophe so a spreadsheet
    /// shows them as text instead of running them as a formula: feedback text comes from strangers.
    /// </summary>
    public static string Row(params string[] cells) => string.Join(',', cells.Select(Cell)) + "\r\n";

    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
