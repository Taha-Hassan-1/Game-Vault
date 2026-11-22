using GameVault.Application.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Infrastructure.Rawg;

/// <summary>Nightly refresh and import. Does nothing unless Rawg:Enabled is set with an API key.</summary>
public sealed class RawgSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RawgOptions _rawg;
    private readonly SyncBudgetOptions _sync;
    private readonly ILogger<RawgSyncWorker> _log;

    public RawgSyncWorker(IServiceScopeFactory scopes, IOptions<RawgOptions> rawg, IOptions<SyncBudgetOptions> sync, ILogger<RawgSyncWorker> log)
    {
        _scopes = scopes;
        _rawg = rawg.Value;
        _sync = sync.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_rawg.Enabled)
        {
            _log.LogInformation("RAWG sync is off (Rawg:Enabled is false)");
            return;
        }

        if (!_sync.SyncEnabled)
        {
            _log.LogInformation("RAWG nightly sync is off (SyncBudget:SyncEnabled is false); details are still fetched when a game page is opened");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(_sync.InitialDelayMinutes), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<GameImportService>().RunSyncAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogError(ex, "RAWG sync run failed; trying again tomorrow");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
