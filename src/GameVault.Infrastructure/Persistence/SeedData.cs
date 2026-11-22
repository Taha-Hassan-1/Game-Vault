using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public static class SeedData
{
    private sealed record Seed(string Title, int Year, string[] Genres, string[] Platforms, string[] Tags, string Blurb, bool Studio = false, string? Cover = null);

    private static readonly string[] GenreNames =
        { "Action", "Adventure", "RPG", "Strategy", "Puzzle", "Platformer", "Shooter", "Simulation", "Racing", "Sports", "Indie", "Roguelike" };
    private static readonly string[] PlatformNames = { "PC", "PlayStation", "Xbox", "Nintendo Switch", "Android", "iOS" };
    private static readonly string[] TagNames =
        { "Singleplayer", "Multiplayer", "Co-op", "Story Rich", "Relaxing", "Difficult", "Open World", "Pixel Graphics",
          "Fantasy", "Sci-fi", "Casual", "Procedural", "Atmospheric", "Great Soundtrack" };

    private static readonly Seed[] Games =
    {
        new("Untangle", 2026, new[]{"Puzzle","Indie"}, new[]{"Android"}, new[]{"Casual","Relaxing","Procedural","Singleplayer"}, "Drag the nodes until no edges cross. Every level is generated from a seed, with a daily challenge and several modes.", true, "/img/studio/untangle.webp"),
        new("Chain Mixer", 2026, new[]{"Puzzle","Indie"}, new[]{"Android"}, new[]{"Casual","Singleplayer"}, "Shoot coloured balls into a moving chain, match colours to pop them and clear the chain before it reaches the end.", true, "/img/studio/chain-mixer.webp"),
        new("The Witcher 3: Wild Hunt", 2015, new[]{"RPG","Adventure"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Open World","Story Rich","Fantasy","Singleplayer"}, "A monster hunter searches a war-torn continent for his adopted daughter."),
        new("Hades", 2020, new[]{"Action","Roguelike","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Difficult","Story Rich","Great Soundtrack","Singleplayer"}, "Fight out of the underworld in a fast roguelike with a story that grows with every escape attempt."),
        new("Celeste", 2018, new[]{"Platformer","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Difficult","Pixel Graphics","Story Rich","Great Soundtrack"}, "Climb a mountain through tight, precise platforming and a heartfelt story."),
        new("Stardew Valley", 2016, new[]{"Simulation","RPG","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch","Android","iOS"}, new[]{"Relaxing","Pixel Graphics","Co-op","Singleplayer"}, "Inherit a farm and build a new life in a small valley town."),
        new("Portal 2", 2011, new[]{"Puzzle","Adventure"}, new[]{"PC","PlayStation","Xbox"}, new[]{"Co-op","Sci-fi","Story Rich","Atmospheric"}, "Solve physics puzzles with a portal gun, alone or with a friend."),
        new("Civilization VI", 2016, new[]{"Strategy"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch","iOS"}, new[]{"Multiplayer","Singleplayer","Difficult"}, "Lead a nation from the stone age to the space age."),
        new("Dark Souls III", 2016, new[]{"Action","RPG"}, new[]{"PC","PlayStation","Xbox"}, new[]{"Difficult","Fantasy","Atmospheric","Multiplayer"}, "Rise, ember, in a ruined world of demanding duels and hidden lore."),
        new("Minecraft", 2011, new[]{"Adventure","Simulation"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch","Android","iOS"}, new[]{"Open World","Multiplayer","Co-op","Procedural"}, "Mine, build and survive in a world made of blocks."),
        new("Hollow Knight", 2017, new[]{"Platformer","Adventure","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Difficult","Atmospheric","Great Soundtrack","Fantasy"}, "Explore a vast, ruined insect kingdom in a hand-drawn action adventure."),
        new("Forza Horizon 5", 2021, new[]{"Racing","Sports"}, new[]{"PC","Xbox"}, new[]{"Open World","Multiplayer","Co-op"}, "Race across a sun-soaked Mexico in an open-world festival."),
        new("Tetris Effect", 2018, new[]{"Puzzle"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Great Soundtrack","Relaxing","Casual","Atmospheric"}, "The classic falling-block puzzle, rebuilt around music and light."),
        new("Monument Valley", 2014, new[]{"Puzzle","Indie"}, new[]{"Android","iOS"}, new[]{"Relaxing","Atmospheric","Casual","Great Soundtrack"}, "Guide a princess through impossible geometry."),
        new("Slay the Spire", 2019, new[]{"Strategy","Roguelike","Indie"}, new[]{"PC","PlayStation","Nintendo Switch","Android","iOS"}, new[]{"Difficult","Procedural","Singleplayer"}, "Build a deck and climb a spire in a card-battling roguelike."),
        new("Terraria", 2011, new[]{"Action","Adventure","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch","Android","iOS"}, new[]{"Open World","Pixel Graphics","Co-op","Procedural"}, "Dig, fight, explore and build in a 2D sandbox."),
        new("Disco Elysium", 2019, new[]{"RPG"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Story Rich","Atmospheric","Singleplayer"}, "A detective with no memory solves a murder in a city that feels alive."),
        new("Rocket League", 2015, new[]{"Sports","Racing"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Multiplayer","Casual","Co-op"}, "Soccer, but with rocket-powered cars."),
        new("Cuphead", 2017, new[]{"Platformer","Action","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Difficult","Co-op","Great Soundtrack"}, "Run-and-gun boss battles drawn like a 1930s cartoon."),
        new("Factorio", 2020, new[]{"Simulation","Strategy","Indie"}, new[]{"PC"}, new[]{"Multiplayer","Co-op","Sci-fi","Difficult"}, "Build factories, automate everything and survive the locals."),
        new("Among Us", 2018, new[]{"Strategy","Indie"}, new[]{"PC","Android","iOS","Nintendo Switch"}, new[]{"Multiplayer","Casual","Sci-fi"}, "Find the impostor before the ship is lost."),
        new("DOOM Eternal", 2020, new[]{"Shooter","Action"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Difficult","Great Soundtrack","Sci-fi","Multiplayer"}, "Rip and tear through hell in a relentless shooter."),
        new("Return of the Obra Dinn", 2018, new[]{"Puzzle","Adventure","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch"}, new[]{"Atmospheric","Story Rich","Singleplayer"}, "Deduce the fate of a ghost ship's crew from frozen moments of death."),
        new("Baldur's Gate 3", 2023, new[]{"RPG","Strategy"}, new[]{"PC","PlayStation","Xbox"}, new[]{"Story Rich","Co-op","Fantasy","Difficult"}, "Gather a party and face a mind-flayer threat in a huge, reactive RPG."),
        new("Super Mario Odyssey", 2017, new[]{"Platformer","Adventure"}, new[]{"Nintendo Switch"}, new[]{"Casual","Singleplayer","Co-op"}, "Hurl a cap across kingdoms in a playful 3D platformer."),
        new("Dead Cells", 2018, new[]{"Action","Roguelike","Platformer","Indie"}, new[]{"PC","PlayStation","Xbox","Nintendo Switch","Android","iOS"}, new[]{"Difficult","Pixel Graphics","Procedural"}, "A fast, fluid roguelite in an ever-changing castle."),
        new("Valheim", 2021, new[]{"Adventure","Simulation","Indie"}, new[]{"PC","Xbox"}, new[]{"Open World","Co-op","Multiplayer","Procedural"}, "Survive and build as a Viking in a procedurally generated purgatory."),
        new("Two Dots", 2014, new[]{"Puzzle"}, new[]{"Android","iOS"}, new[]{"Casual","Relaxing","Singleplayer"}, "Connect dots in a calm mobile puzzle."),
        new("Alto's Odyssey", 2018, new[]{"Platformer","Indie"}, new[]{"Android","iOS","PC","Nintendo Switch"}, new[]{"Relaxing","Atmospheric","Casual","Great Soundtrack"}, "Sandboard across endless desert dunes."),
        new("Mini Metro", 2015, new[]{"Strategy","Puzzle","Simulation","Indie"}, new[]{"PC","Android","iOS","Nintendo Switch"}, new[]{"Relaxing","Casual","Procedural","Great Soundtrack"}, "Draw subway lines for a city that never stops growing."),
        new("Outer Wilds", 2019, new[]{"Adventure","Puzzle","Indie"}, new[]{"PC","PlayStation","Xbox"}, new[]{"Open World","Story Rich","Sci-fi","Atmospheric"}, "Explore a solar system caught in a 22-minute time loop."),
    };

    public static string Slugify(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    public static async Task EnsureSeededAsync(GameVaultDbContext db, CancellationToken ct = default)
    {
        if (await db.Games.AnyAsync(ct))
        {
            await EnsureStudioGamesAsync(db, ct);
            return;
        }

        var genres = GenreNames.Select(n => new Genre { Name = n, Slug = Slugify(n) }).ToDictionary(x => x.Name);
        var platforms = PlatformNames.Select(n => new Platform { Name = n, Slug = Slugify(n) }).ToDictionary(x => x.Name);
        var tags = TagNames.Select(n => new Tag { Name = n, Slug = Slugify(n) }).ToDictionary(x => x.Name);
        db.Genres.AddRange(genres.Values);
        db.Platforms.AddRange(platforms.Values);
        db.Tags.AddRange(tags.Values);

        // Staggered creation times keep "recently added" deterministic and the studio game on top.
        var start = DateTime.UtcNow.AddDays(-Games.Length);
        for (var i = 0; i < Games.Length; i++)
        {
            var s = Games[i];
            db.Games.Add(new Game
            {
                Slug = Slugify(s.Title),
                Title = s.Title,
                Description = s.Blurb,
                ReleaseDate = new DateOnly(s.Year, 1, 1),
                Source = s.Studio ? GameSource.Studio : GameSource.Rawg,
                IsStudioGame = s.Studio,
                CoverUrl = s.Cover,
                CreatedAt = s.Studio ? DateTime.UtcNow : start.AddDays(i),
                Genres = s.Genres.Select(n => genres[n]).ToList(),
                Platforms = s.Platforms.Select(n => platforms[n]).ToList(),
                Tags = s.Tags.Select(n => tags[n]).ToList()
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// For a database that was seeded by an earlier version: adds any studio game that is missing and gives a studio game
    /// its shipped cover when it has none. A cover the owner set is never replaced.
    /// </summary>
    private static async Task EnsureStudioGamesAsync(GameVaultDbContext db, CancellationToken ct)
    {
        foreach (var s in Games.Where(g => g.Studio))
        {
            var slug = Slugify(s.Title);
            var existing = await db.Games.FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (existing is not null)
            {
                if (existing.CoverUrl is null && s.Cover is not null) existing.CoverUrl = s.Cover;
                continue;
            }

            var genres = new List<Genre>();
            foreach (var n in s.Genres) genres.Add(await FindOrNewAsync(db.Genres, g => g.Slug == Slugify(n), () => new Genre { Name = n, Slug = Slugify(n) }, ct));
            var platforms = new List<Platform>();
            foreach (var n in s.Platforms) platforms.Add(await FindOrNewAsync(db.Platforms, g => g.Slug == Slugify(n), () => new Platform { Name = n, Slug = Slugify(n) }, ct));
            var tags = new List<Tag>();
            foreach (var n in s.Tags) tags.Add(await FindOrNewAsync(db.Tags, g => g.Slug == Slugify(n), () => new Tag { Name = n, Slug = Slugify(n) }, ct));

            db.Games.Add(new Game
            {
                Slug = slug, Title = s.Title, Description = s.Blurb, ReleaseDate = new DateOnly(s.Year, 1, 1),
                Source = GameSource.Studio, IsStudioGame = true, CoverUrl = s.Cover, CreatedAt = DateTime.UtcNow,
                Genres = genres, Platforms = platforms, Tags = tags
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<T> FindOrNewAsync<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create, CancellationToken ct) where T : class
        => set.Local.AsQueryable().FirstOrDefault(match) ?? await set.FirstOrDefaultAsync(match, ct) ?? create();
}
