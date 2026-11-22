using GameVault.Domain;
using GameVault.Domain.Entities;
using GameVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GameVault.Benchmarks;

/// <summary>A throwaway database with a RAWG-sized synthetic catalogue, so timings mean something. Never the dev or test database.</summary>
public static class PerfDb
{
    public const string Name = "GameVault_Perf";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("GAMEVAULT_PERF_CONNECTION")
        ?? $@"Server=.\SQLEXPRESS;Database={Name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public static DbContextOptions<GameVaultDbContext> Options(params IInterceptor[] interceptors)
    {
        var b = new DbContextOptionsBuilder<GameVaultDbContext>().UseSqlServer(ConnectionString, s => s.CommandTimeout(120));
        if (interceptors.Length > 0) b.AddInterceptors(interceptors);
        return b.Options;
    }

    public static void Seed(int games)
    {
        using var db = new GameVaultDbContext(Options());
        Console.WriteLine($"Recreating {Name} and migrating...");
        db.Database.EnsureDeleted();
        db.Database.Migrate();

        var rnd = new Random(20260101); // fixed seed: the same catalogue every time, so before and after runs are comparable
        var genres = Enumerable.Range(1, 19).Select(i => new Genre { Name = $"Genre {i:00}", Slug = $"genre-{i:00}" }).ToList();
        var platforms = Enumerable.Range(1, 12).Select(i => new Platform { Name = $"Platform {i:00}", Slug = $"platform-{i:00}" }).ToList();
        var tags = Enumerable.Range(1, 300).Select(i => new Tag { Name = $"Tag {i:000}", Slug = $"tag-{i:000}" }).ToList();
        db.Genres.AddRange(genres); db.Platforms.AddRange(platforms); db.Tags.AddRange(tags);

        var users = Enumerable.Range(1, 200).Select(i => new AppUser
        {
            Id = Guid.NewGuid().ToString(), UserName = $"perf{i}@example.test", NormalizedUserName = $"PERF{i}@EXAMPLE.TEST",
            Email = $"perf{i}@example.test", NormalizedEmail = $"PERF{i}@EXAMPLE.TEST", DisplayName = $"Perf User {i}",
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        }).ToList();
        db.Users.AddRange(users);
        db.SaveChanges();

        var words = "puzzle quest dragon neon pixel racing galaxy kingdom shadow turbo mystic arena forge harbour echo ember frost orbit relic saga vault zenith".Split(' ');
        string Sentence(int n) => string.Join(' ', Enumerable.Range(0, n).Select(_ => words[rnd.Next(words.Length)]));
        T[] Pick<T>(IList<T> source, int count, bool skewed)
        {
            var set = new HashSet<int>();
            while (set.Count < count) set.Add(skewed ? (int)(Math.Pow(rnd.NextDouble(), 2.2) * source.Count) : rnd.Next(source.Count));
            return set.Select(i => source[i]).ToArray();
        }

        var start = DateTime.UtcNow.AddMinutes(-games);
        for (var batch = 0; batch < games; batch += 250)
        {
            for (var i = batch; i < Math.Min(batch + 250, games); i++)
            {
                var g = new Game
                {
                    Slug = $"synthetic-game-{i:0000}", Title = $"{Sentence(2)} {i:0000}", Description = Sentence(rnd.Next(70, 140)),
                    ReleaseDate = new DateOnly(1995 + rnd.Next(32), 1 + rnd.Next(12), 1 + rnd.Next(28)),
                    Source = GameSource.Rawg, ExternalId = i.ToString(), CreatedAt = start.AddMinutes(i), CoverUrl = $"https://media.example.test/games/{i}.jpg",
                    Genres = Pick(genres, 1 + rnd.Next(3), false).ToList(), Platforms = Pick(platforms, 1 + rnd.Next(4), false).ToList(),
                    Tags = Pick(tags, 5 + rnd.Next(6), true).ToList()
                };
                var reviewers = Pick(users, rnd.Next(0, 7), false);
                foreach (var u in reviewers)
                {
                    var r = new Review { UserId = u.Id, CreatedAt = start.AddMinutes(i) };
                    r.Write(1 + rnd.Next(5), Sentence(rnd.Next(10, 40)));
                    g.Reviews.Add(r);
                }
                g.ApplyRating(g.Reviews.Count, g.Reviews.Count == 0 ? 0 : g.Reviews.Average(r => r.Stars));
                db.Games.Add(g);
            }
            db.SaveChanges();
            db.ChangeTracker.Clear();
            // Cleared entities would be inserted again as new rows by the next batch; attach them as existing.
            db.AttachRange(genres); db.AttachRange(platforms); db.AttachRange(tags);
            Console.Write($"\r{Math.Min(batch + 250, games)}/{games} games");
        }
        Console.WriteLine();
        using var cn = new SqlConnection(ConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT (SELECT COUNT(*) FROM Games), (SELECT COUNT(*) FROM Reviews), (SELECT COUNT(*) FROM GameTag), (SELECT COUNT(*) FROM GameGenre)";
        using var rd = cmd.ExecuteReader();
        rd.Read();
        Console.WriteLine($"Seeded: {rd.GetInt32(0)} games, {rd.GetInt32(1)} reviews, {rd.GetInt32(2)} game-tag rows, {rd.GetInt32(3)} game-genre rows.");
    }
}

/// <summary>Counts the commands EF sends and tags where the time goes.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Count { get; private set; }
    public void Reset() => Count = 0;

    public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(System.Data.Common.DbCommand command,
        CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result) { Count++; return result; }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
        CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
    { Count++; return ValueTask.FromResult(result); }
}
