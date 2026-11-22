using GameVault.Application.Catalogue;
using GameVault.Application.Metadata;
using GameVault.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly.CircuitBreaker;

namespace GameVault.Tests.Rawg;

public class ImportServiceTests
{
    private readonly IGameMetadataProvider _provider = Substitute.For<IGameMetadataProvider>();
    private readonly IExternalGameStore _store = Substitute.For<IExternalGameStore>();
    private readonly IRequestBudget _budget = Substitute.For<IRequestBudget>();
    private readonly IGameCatalog _catalog = Substitute.For<IGameCatalog>();

    private static ExternalGame Game(string id) => new(id, "g" + id, "Game " + id, "d", null, null,
        Array.Empty<ExternalTaxon>(), Array.Empty<ExternalTaxon>(), Array.Empty<ExternalTaxon>(), Array.Empty<string>(), true);

    private GameImportService Service(bool enabled = true, int pages = 3, int refresh = 10)
    {
        _provider.Source.Returns(GameSource.Rawg);
        _budget.CanSyncAsync(Arg.Any<CancellationToken>()).Returns(true);
        return new GameImportService(
            new GameMetadataProviderFactory(new[] { _provider }), _store, _budget, _catalog,
            Options.Create(new RawgOptions { Enabled = enabled, ApiKey = "k" }),
            Options.Create(new SyncBudgetOptions { PagesPerRun = pages, RefreshPerRun = refresh }),
            TimeProvider.System, NullLogger<GameImportService>.Instance);
    }

    [Fact]
    public async Task Disabled_integration_does_nothing()
    {
        var s = Service(enabled: false);
        Assert.False(await s.EnsureDetailsAsync("x"));
        Assert.Equal(new SyncResult(0, 0, false), await s.RunSyncAsync());
        await _provider.DidNotReceiveWithAnyArgs().GetByExternalIdAsync(default!);
    }

    [Fact]
    public async Task Details_are_fetched_stored_and_the_cache_is_cleared()
    {
        _store.GetExternalIdNeedingDetailsAsync("x", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns("7");
        _provider.GetByExternalIdAsync("7", Arg.Any<CancellationToken>()).Returns(Game("7"));

        Assert.True(await Service().EnsureDetailsAsync("X"));

        await _store.Received(1).UpsertAsync(Arg.Is<IReadOnlyList<ExternalGame>>(l => l.Count == 1), Arg.Any<CancellationToken>());
        await _catalog.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Games_with_fresh_details_make_no_call()
    {
        _store.GetExternalIdNeedingDetailsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        Assert.False(await Service().EnsureDetailsAsync("x"));
        await _provider.DidNotReceiveWithAnyArgs().GetByExternalIdAsync(default!);
    }

    [Theory]
    [InlineData(typeof(BrokenCircuitException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(BudgetExceededException))]
    public async Task A_provider_outage_is_swallowed_so_the_page_still_renders(Type failure)
    {
        _store.GetExternalIdNeedingDetailsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns("7");
        var ex = failure == typeof(BudgetExceededException) ? new BudgetExceededException("rawg") : (Exception)Activator.CreateInstance(failure)!;
        _provider.GetByExternalIdAsync("7", Arg.Any<CancellationToken>()).ThrowsAsync(ex);

        Assert.False(await Service().EnsureDetailsAsync("x"));

        await _store.DidNotReceiveWithAnyArgs().UpsertAsync(default!);
    }

    [Fact]
    public async Task Sync_refreshes_stale_rows_then_imports_pages_and_advances_the_cursor()
    {
        _store.GetStaleExternalIdsAsync(Arg.Any<DateTime>(), 10, Arg.Any<CancellationToken>()).Returns(new[] { "1", "2" });
        _provider.GetByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci => Game((string)ci[0]));
        _store.GetSyncPageAsync(Arg.Any<CancellationToken>()).Returns(5, 6);
        _provider.SearchAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Game("10"), Game("11") }, Array.Empty<ExternalGame>());
        _store.UpsertAsync(Arg.Is<IReadOnlyList<ExternalGame>>(l => l.Count == 2), Arg.Any<CancellationToken>()).Returns(2);

        var result = await Service(pages: 3).RunSyncAsync();

        Assert.Equal(2, result.Refreshed);
        Assert.Equal(2, result.Imported);
        await _store.Received(1).SetSyncPageAsync(6, Arg.Any<CancellationToken>());
        await _catalog.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_stops_as_soon_as_the_sync_budget_is_spent()
    {
        _store.GetStaleExternalIdsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { "1" });
        var s = Service();
        _budget.CanSyncAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await s.RunSyncAsync();

        Assert.True(result.StoppedForBudget);
        await _provider.DidNotReceiveWithAnyArgs().GetByExternalIdAsync(default!);
        await _provider.DidNotReceiveWithAnyArgs().SearchAsync(default, default, default);
    }

    [Fact]
    public async Task Sync_survives_an_open_circuit_and_reports_what_it_did()
    {
        _store.GetStaleExternalIdsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        _store.GetSyncPageAsync(Arg.Any<CancellationToken>()).Returns(1);
        _provider.SearchAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).ThrowsAsync(new BrokenCircuitException());

        var result = await Service().RunSyncAsync();

        Assert.Equal(new SyncResult(0, 0, false), result);
    }
}

public class RequestBudgetTests
{
    private static (RequestBudget Budget, FakeUsageStore Store) Make(int total)
    {
        var store = new FakeUsageStore { Total = total };
        return (new RequestBudget(store, Options.Create(new SyncBudgetOptions()), TimeProvider.System), store);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(14_999, true)]
    [InlineData(15_000, false)]
    public async Task Sync_may_spend_up_to_75_percent(int used, bool allowed)
        => Assert.Equal(allowed, await Make(used).Budget.CanSyncAsync());

    [Theory]
    [InlineData(19_999, true, 1)]
    [InlineData(20_000, false, 0)]
    public async Task On_demand_requests_may_use_the_whole_monthly_limit(int used, bool allowed, int counted)
    {
        var (budget, store) = Make(used);
        Assert.Equal(allowed, await budget.TryConsumeAsync());
        Assert.Equal(counted, store.Increments);
    }
}
