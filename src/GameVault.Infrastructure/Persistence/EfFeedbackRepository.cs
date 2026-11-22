using GameVault.Application.Catalogue;
using GameVault.Application.Feedback;
using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public sealed class EfFeedbackRepository : IFeedbackRepository
{
    private readonly GameVaultDbContext _db;
    public EfFeedbackRepository(GameVaultDbContext db) => _db = db;

    public async Task<int> AddAsync(FeedbackMessage message, CancellationToken ct = default)
    {
        _db.FeedbackMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message.Id;
    }

    public async Task<PagedResult<FeedbackItem>> ListAsync(FeedbackFilter f, CancellationToken ct = default)
    {
        var query = Filtered(f);
        var total = await query.CountAsync(ct);
        var items = await Items(query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)).ToListAsync(ct);
        return new PagedResult<FeedbackItem>(items, f.Page, f.PageSize, total);
    }

    public async Task<IReadOnlyList<FeedbackItem>> ExportAsync(FeedbackFilter f, int limit, CancellationToken ct = default)
        => await Items(Filtered(f).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit)).ToListAsync(ct);

    public async Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)
        => await _db.FeedbackMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status), ct) > 0;

    public async Task<StudioDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var games = await _db.Games.AsNoTracking().Where(g => g.IsStudioGame).OrderBy(g => g.Title)
            .Select(g => new StudioGameSummary(g.Id, g.Slug, g.Title, g.RatingAverage, g.RatingCount,
                _db.FeedbackMessages.Count(m => m.GameId == g.Id && m.Status == FeedbackStatus.New),
                _db.Suggestions.Count(s => s.GameId == g.Id
                    && (s.Status == SuggestionStatus.New || s.Status == SuggestionStatus.Planned || s.Status == SuggestionStatus.InProgress))))
            .ToListAsync(ct);

        var newest = await Items(_db.FeedbackMessages.AsNoTracking().OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(8)).ToListAsync(ct);

        var versions = await _db.FeedbackMessages.AsNoTracking()
            .Where(m => m.AppVersion != null)
            .GroupBy(m => new { m.GameId, m.AppVersion })
            .Select(g => new
            {
                g.Key.GameId, Version = g.Key.AppVersion!, Latest = g.Max(m => m.CreatedAt),
                Bugs = g.Count(m => m.Category == FeedbackCategory.Bug),
                Ideas = g.Count(m => m.Category == FeedbackCategory.Idea),
                Praise = g.Count(m => m.Category == FeedbackCategory.Praise)
            })
            .OrderByDescending(x => x.Latest).Take(30).ToListAsync(ct);

        var titles = games.ToDictionary(g => g.Id, g => g.Title);
        var rows = versions.Where(v => titles.ContainsKey(v.GameId))
            .Select(v => new VersionRow(titles[v.GameId], v.Version, v.Bugs, v.Ideas, v.Praise)).ToList();
        return new StudioDashboard(games, newest, rows);
    }

    private IQueryable<FeedbackMessage> Filtered(FeedbackFilter f)
    {
        var q = _db.FeedbackMessages.AsNoTracking().AsQueryable();
        if (f.GameId is { } g) q = q.Where(m => m.GameId == g);
        if (f.Category is { } c) q = q.Where(m => m.Category == c);
        if (f.Status is { } s) q = q.Where(m => m.Status == s);
        if (f.Source is { } src) q = q.Where(m => m.Source == src);
        return q;
    }

    private static IQueryable<FeedbackItem> Items(IQueryable<FeedbackMessage> q)
        => q.Select(m => new FeedbackItem(m.Id, m.GameId, m.Game!.Title, m.Source, m.Category, m.Body,
            m.PlayerRef, m.AppVersion, m.Status, m.CreatedAt));
}
