using GameVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public class StudioSeedTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public StudioSeedTests(GameVaultFactory factory) => _factory = factory;

    private async Task<GameVaultDbContext> BootAsync(IServiceScope scope)
    {
        await Web.NewClient(_factory).GetAsync("/"); // boots the host, which seeds
        return scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
    }

    [Fact]
    public async Task A_fresh_database_has_both_studio_games_with_their_covers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = await BootAsync(scope);

        var studio = await db.Games.Where(g => g.IsStudioGame).ToDictionaryAsync(g => g.Slug);
        Assert.Equal("/img/studio/untangle.webp", studio["untangle"].CoverUrl);
        Assert.Equal("/img/studio/chain-mixer.webp", studio["chain-mixer"].CoverUrl);
    }

    [Fact]
    public async Task Seeding_again_restores_a_missing_studio_game_and_a_missing_cover_but_never_replaces_the_owners_cover()
    {
        using var scope = _factory.Services.CreateScope();
        var db = await BootAsync(scope);

        // The shape of a database seeded by the first version: no Chain Mixer, no Untangle cover.
        var chain = await db.Games.SingleAsync(g => g.Slug == "chain-mixer");
        db.Games.Remove(chain);
        var untangle = await db.Games.SingleAsync(g => g.Slug == "untangle");
        untangle.CoverUrl = null;
        await db.SaveChangesAsync();

        await SeedData.EnsureSeededAsync(db);

        Assert.Equal("/img/studio/chain-mixer.webp", (await db.Games.AsNoTracking().SingleAsync(g => g.Slug == "chain-mixer")).CoverUrl);
        Assert.Equal("/img/studio/untangle.webp", (await db.Games.AsNoTracking().SingleAsync(g => g.Slug == "untangle")).CoverUrl);
        Assert.Equal(1, await db.Games.CountAsync(g => g.Slug == "chain-mixer"));

        // A cover the owner chose stays.
        var again = await db.Games.SingleAsync(g => g.Slug == "untangle");
        again.CoverUrl = "https://example.com/mine.png";
        await db.SaveChangesAsync();
        await SeedData.EnsureSeededAsync(db);
        Assert.Equal("https://example.com/mine.png", (await db.Games.AsNoTracking().SingleAsync(g => g.Slug == "untangle")).CoverUrl);
    }
}
