using GameVault.Application.Metadata;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public class ExternalStoreIntegrationTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public ExternalStoreIntegrationTests(GameVaultFactory factory)
    {
        _factory = factory;
        _ = factory.CreateClient(); // boots the host, which migrates and seeds
    }

    private static ExternalGame Ext(string id, string slug, string title, bool detailed = false, params string[] genres) => new(
        id, slug, title, detailed ? "Full description" : null, new DateOnly(2020, 1, 1), "https://img/c.jpg",
        genres.Select(g => new ExternalTaxon(g, g.ToLowerInvariant())).ToList(),
        new[] { new ExternalTaxon("PC", "pc") }, Array.Empty<ExternalTaxon>(),
        detailed ? new[] { "https://img/1.jpg", "https://img/2.jpg" } : Array.Empty<string>(), detailed);

    [Fact]
    public async Task Upsert_adopts_a_seeded_game_instead_of_duplicating_it_and_reuses_taxonomy()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalGameStore>();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var before = await db.Games.CountAsync();
        var genresBefore = await db.Genres.CountAsync();

        var created = await store.UpsertAsync(new[]
        {
            Ext("28", "hades", "Hades", false, "Action"),            // seeded slug: adopt
            Ext("555", "brand-new-game", "Brand New Game", false, "Action", "Metroidvania")
        });

        Assert.Equal(1, created);
        db.ChangeTracker.Clear();
        Assert.Equal(before + 1, await db.Games.CountAsync());
        Assert.Equal("28", (await db.Games.SingleAsync(g => g.Slug == "hades")).ExternalId);
        Assert.Equal(genresBefore + 1, await db.Genres.CountAsync()); // only Metroidvania is new
    }

    [Fact]
    public async Task Details_are_stored_and_only_requested_while_missing_or_stale()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalGameStore>();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        await store.UpsertAsync(new[] { Ext("901", "detail-game", "Detail Game") });

        var future = DateTime.UtcNow.AddDays(-30);
        Assert.Equal("901", await store.GetExternalIdNeedingDetailsAsync("detail-game", future));

        await store.UpsertAsync(new[] { Ext("901", "detail-game", "Detail Game", true, "Action") });

        Assert.Null(await store.GetExternalIdNeedingDetailsAsync("detail-game", future));
        Assert.Equal("901", await store.GetExternalIdNeedingDetailsAsync("detail-game", DateTime.UtcNow.AddDays(1)));
        db.ChangeTracker.Clear();
        var game = await db.Games.Include(g => g.Screenshots).SingleAsync(g => g.Slug == "detail-game");
        Assert.Equal("Full description", game.Description);
        Assert.Equal(2, game.Screenshots.Count);

        // A later list refresh must not wipe the description.
        await store.UpsertAsync(new[] { Ext("901", "detail-game", "Detail Game") });
        db.ChangeTracker.Clear();
        Assert.Equal("Full description", (await db.Games.SingleAsync(g => g.Slug == "detail-game")).Description);
    }

    [Fact]
    public async Task A_slug_taken_by_another_game_gets_the_external_id_suffix()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalGameStore>();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();

        await store.UpsertAsync(new[] { Ext("700", "untangle", "Untangle (RAWG)") }); // studio game owns "untangle"

        db.ChangeTracker.Clear();
        Assert.True(await db.Games.AnyAsync(g => g.Slug == "untangle-700" && g.ExternalId == "700"));
        Assert.True((await db.Games.SingleAsync(g => g.Slug == "untangle")).IsStudioGame);
    }

    [Fact]
    public async Task Usage_is_counted_per_day_and_summed_per_month_and_the_sync_cursor_persists()
    {
        using var scope = _factory.Services.CreateScope();
        var usage = scope.ServiceProvider.GetRequiredService<IApiUsageStore>();
        var store = scope.ServiceProvider.GetRequiredService<IExternalGameStore>();
        var day = new DateOnly(2031, 3, 10);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => usage.IncrementAsync("rawg", day)));
        await usage.IncrementAsync("rawg", day.AddDays(5));
        await usage.IncrementAsync("rawg", new DateOnly(2031, 4, 1)); // first day of the next month must not count

        Assert.Equal(9, await usage.GetMonthTotalAsync("rawg", day));
        Assert.Equal(0, await usage.GetMonthTotalAsync("other", day));

        Assert.Equal(1, await store.GetSyncPageAsync());
        await store.SetSyncPageAsync(4);
        await store.SetSyncPageAsync(5);
        Assert.Equal(5, await store.GetSyncPageAsync());
    }
}
