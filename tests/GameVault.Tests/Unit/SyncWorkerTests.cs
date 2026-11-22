using GameVault.Application.Metadata;
using GameVault.Infrastructure.Rawg;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GameVault.Tests.Unit;

public class SyncWorkerTests
{
    private static async Task<IServiceScopeFactory> RunAsync(bool rawgEnabled, bool syncEnabled)
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        var worker = new RawgSyncWorker(
            scopes,
            Options.Create(new RawgOptions { Enabled = rawgEnabled, ApiKey = "k" }),
            Options.Create(new SyncBudgetOptions { SyncEnabled = syncEnabled, InitialDelayMinutes = 0 }),
            NullLogger<RawgSyncWorker>.Instance);

        // ExecuteAsync returns at once when the sync is off; when it is on it would start a run, so cancel it.
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        return scopes;
    }

    [Fact]
    public async Task The_nightly_sync_does_nothing_when_SyncEnabled_is_false_even_with_RAWG_on()
    {
        var scopes = await RunAsync(rawgEnabled: true, syncEnabled: false);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task The_nightly_sync_does_nothing_when_RAWG_is_off()
    {
        var scopes = await RunAsync(rawgEnabled: false, syncEnabled: true);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task The_nightly_sync_starts_a_run_when_both_are_on()
    {
        var scopes = await RunAsync(rawgEnabled: true, syncEnabled: true);
        scopes.ReceivedWithAnyArgs().CreateScope();
    }
}
