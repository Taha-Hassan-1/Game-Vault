using GameVault.Application.Catalogue;
using GameVault.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Application.Metadata;

public sealed record SyncResult(int Refreshed, int Imported, bool StoppedForBudget);

public sealed class GameImportService
{
    private readonly IGameMetadataProviderFactory _providers;
    private readonly IExternalGameStore _store;
    private readonly IRequestBudget _budget;
    private readonly IGameCatalog _catalog;
    private readonly RawgOptions _rawg;
    private readonly SyncBudgetOptions _sync;
    private readonly TimeProvider _time;
    private readonly ILogger<GameImportService> _log;

    public GameImportService(
        IGameMetadataProviderFactory providers, IExternalGameStore store, IRequestBudget budget, IGameCatalog catalog,
        IOptions<RawgOptions> rawg, IOptions<SyncBudgetOptions> sync, TimeProvider time, ILogger<GameImportService> log)
    {
        _providers = providers;
        _store = store;
        _budget = budget;
        _catalog = catalog;
        _rawg = rawg.Value;
        _sync = sync.Value;
        _time = time;
        _log = log;
    }

    private DateTime StaleBefore => _time.GetUtcNow().UtcDateTime.AddDays(-_rawg.StaleAfterDays);

    /// <summary>
    /// Fetches description and screenshots the first time a game page is opened (or after they go stale).
    /// A provider outage is logged and swallowed: the page renders from the database copy, never a 500.
    /// </summary>
    public async Task<bool> EnsureDetailsAsync(string slug, CancellationToken ct = default)
    {
        if (!_rawg.Enabled) return false;
        var provider = _providers.Get(GameSource.Rawg);
        if (provider is null) return false;

        var externalId = await _store.GetExternalIdNeedingDetailsAsync(slug.ToLowerInvariant(), StaleBefore, ct);
        if (externalId is null) return false;

        try
        {
            var game = await provider.GetByExternalIdAsync(externalId, ct);
            if (game is null) return false;
            await _store.UpsertAsync(new[] { game }, ct);
            await _catalog.InvalidateAsync(ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "RAWG details for {Slug} unavailable; serving the stored copy", slug);
            return false;
        }
    }

    /// <summary>One nightly pass: refresh the stalest rows first, then import the next pages of popular games, always inside the budget.</summary>
    public async Task<SyncResult> RunSyncAsync(CancellationToken ct = default)
    {
        var provider = _providers.Get(GameSource.Rawg);
        if (!_rawg.Enabled || provider is null) return new SyncResult(0, 0, false);

        int refreshed = 0, imported = 0;
        var budgetHit = false;
        try
        {
            foreach (var id in await _store.GetStaleExternalIdsAsync(StaleBefore, _sync.RefreshPerRun, ct))
            {
                if (!await _budget.CanSyncAsync(ct)) { budgetHit = true; break; }
                var game = await provider.GetByExternalIdAsync(id, ct);
                if (game is null) continue;
                await _store.UpsertAsync(new[] { game }, ct);
                refreshed++;
            }

            for (var i = 0; i < _sync.PagesPerRun && !budgetHit; i++)
            {
                if (!await _budget.CanSyncAsync(ct)) { budgetHit = true; break; }
                var page = await _store.GetSyncPageAsync(ct);
                var rows = await provider.SearchAsync(null, page, _rawg.PageSize, ct);
                if (rows.Count == 0) break;
                imported += await _store.UpsertAsync(rows, ct);
                await _store.SetSyncPageAsync(page + 1, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Includes an open circuit or an exhausted budget; the next night continues from the saved page.
            _log.LogWarning(ex, "RAWG sync stopped early after {Refreshed} refreshed and {Imported} imported", refreshed, imported);
            budgetHit = ex is BudgetExceededException;
        }

        if (refreshed + imported > 0) await _catalog.InvalidateAsync(ct);
        _log.LogInformation("RAWG sync done: {Refreshed} refreshed, {Imported} new, budget stop {Budget}", refreshed, imported, budgetHit);
        return new SyncResult(refreshed, imported, budgetHit);
    }
}
