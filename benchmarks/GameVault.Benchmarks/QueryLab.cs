using System.Diagnostics;
using System.Text.RegularExpressions;
using GameVault.Application.Catalogue;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Benchmarks;

/// <summary>
/// Times each query variant the way a request would run it: a fresh DbContext every run, a warm-up first, then the median and
/// 95th percentile of many runs, plus the number of SQL commands and the logical reads SQL Server reports.
/// </summary>
public static class QueryLab
{
    private const int Runs = 40;

    private sealed record Row(string Name, double MedianMs, double P95Ms, int Queries, long Reads);

    private static readonly List<Row> Rows = new();

    /// <summary>Logical page reads this session has done so far, from the server's own counter.</summary>
    private static async Task<long> SessionReads(SqlConnection cn)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT logical_reads FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task Measure(string name, Func<GameVaultDbContext, Task> action)
    {
        var counter = new CommandCounter();
        var times = new List<double>();
        long reads = 0;
        var queries = 0;

        for (var i = 0; i < Runs + 3; i++)
        {
            await using var db = new GameVaultDbContext(PerfDb.Options(counter));
            var cn = (SqlConnection)db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(); // one connection, so the session's read counter covers exactly this run

            var readsBefore = await SessionReads(cn);
            counter.Reset();
            var sw = Stopwatch.StartNew();
            await action(db);
            sw.Stop();
            var runReads = await SessionReads(cn) - readsBefore - 0; // the counter query itself reads nothing from user tables

            if (i < 3) continue; // warm-up: plan cache, connection pool, JIT
            times.Add(sw.Elapsed.TotalMilliseconds);
            reads = runReads;
            queries = counter.Count;
        }

        times.Sort();
        Rows.Add(new Row(name, times[times.Count / 2], times[(int)(times.Count * 0.95)], queries, reads));
    }

    public static async Task RunAsync(string label)
    {
        Rows.Clear();
        await using (var db = new GameVaultDbContext(PerfDb.Options()))
            Console.WriteLine($"Catalogue size: {await db.Games.CountAsync()} games. Label: {label}. {Runs} runs each, median and p95 in ms.\n");

        // ---- Reads: shipped projections versus the entity-graph approach ----
        await Measure("catalogue page, SHIPPED (projection, server paging)", async db =>
            await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Page: 1, PageSize: 12).Normalized()));
        await Measure("catalogue page, NAIVE (tracked graph, paged in memory)", async db =>
        {
            var all = await db.Games.Include(g => g.Genres).Include(g => g.Platforms).Include(g => g.Tags).AsSplitQuery()
                .OrderByDescending(g => g.CreatedAt).ToListAsync();
            _ = all.Skip(0).Take(12).ToList();
        });

        // ---- N+1 ----
        await Measure("12 cards with genres, SHIPPED (one projected query + count)", async db =>
            await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Page: 1, PageSize: 12).Normalized()));
        await Measure("12 cards with genres, NAIVE N+1 (a genre query per game)", async db =>
        {
            var games = await db.Games.AsNoTracking().OrderByDescending(g => g.CreatedAt).Take(12).ToListAsync();
            await db.Games.CountAsync();
            foreach (var g in games) _ = await db.Genres.AsNoTracking().Where(x => x.Games.Any(y => y.Id == g.Id)).ToListAsync();
        });

        // ---- Sorting and paging (the indexes matter here) ----
        foreach (var sort in new[] { GameSort.Newest, GameSort.Released, GameSort.TopRated, GameSort.Title })
        {
            await Measure($"sort {sort}, page 1", async db => await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Sort: sort, Page: 1).Normalized()));
            await Measure($"sort {sort}, page 200 (deep)", async db => await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Sort: sort, Page: 200).Normalized()));
        }
        await Measure("filter genre-03 + TopRated, page 1", async db =>
            await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Genre: "genre-03", Sort: GameSort.TopRated).Normalized()));

        // ---- Search ----
        await Measure("search 'dragon' in title OR description, SHIPPED (LIKE %term%)", async db =>
            await new EfGameRepository(db).SearchAsync(new CatalogueQuery(Search: "dragon").Normalized()));
        await Measure("search 'dragon' in title only (LIKE %term%)", async db =>
        {
            var q = db.Games.AsNoTracking().Where(g => EF.Functions.Like(g.Title, "%dragon%"));
            await q.CountAsync();
            _ = await q.OrderByDescending(g => g.CreatedAt).Take(12).Select(g => g.Title).ToListAsync();
        });
        await Measure("search 'dragon' title prefix (LIKE term%, can seek the Title index)", async db =>
        {
            var q = db.Games.AsNoTracking().Where(g => EF.Functions.Like(g.Title, "dragon%"));
            await q.CountAsync();
            _ = await q.OrderByDescending(g => g.CreatedAt).Take(12).Select(g => g.Title).ToListAsync();
        });

        // ---- Denormalised rating versus aggregate on read ----
        await Measure("top rated page, SHIPPED (RatingAverage column + index)", async db =>
            _ = await db.Games.AsNoTracking().OrderByDescending(g => g.RatingAverage).ThenByDescending(g => g.RatingCount).Take(12)
                .Select(g => new { g.Title, g.RatingAverage, g.RatingCount }).ToListAsync());
        await Measure("top rated page, NAIVE (AVG over reviews on every read)", async db =>
            _ = await db.Games.AsNoTracking()
                .Select(g => new { g.Title, Avg = g.Reviews.Where(r => !r.IsHidden).Average(r => (double?)r.Stars) ?? 0, Cnt = g.Reviews.Count(r => !r.IsHidden) })
                .OrderByDescending(x => x.Avg).ThenByDescending(x => x.Cnt).Take(12).ToListAsync());

        // ---- Similar games: how the 200 candidates are loaded ----
        await Measure("similar candidates x200, SHIPPED (M5: split query; M6: flat rows)", async db =>
            await new EfGameRepository(db).GetCandidatePoolAsync("synthetic-game-0000", 200));
        await Measure("similar candidates x200, single joined query", async db =>
        {
            var rows = await db.Games.AsNoTracking().Where(g => g.Slug != "synthetic-game-0000")
                .OrderByDescending(g => g.RatingAverage).ThenByDescending(g => g.RatingCount).ThenBy(g => g.Title).ThenBy(g => g.Id).Take(200)
                .Select(g => new { g.Id, G = g.Genres.Select(x => x.Slug).ToList(), T = g.Tags.Select(x => x.Slug).ToList() }).ToListAsync();
            _ = rows.Count;
        });
        await Measure("similar candidates x200, flat rows (3 plain queries)", async db =>
        {
            var ids = await db.Games.AsNoTracking().Where(g => g.Slug != "synthetic-game-0000")
                .OrderByDescending(g => g.RatingAverage).ThenByDescending(g => g.RatingCount).ThenBy(g => g.Title).ThenBy(g => g.Id)
                .Take(200).Select(g => g.Id).ToListAsync();
            _ = await db.Games.AsNoTracking().Where(g => ids.Contains(g.Id)).SelectMany(g => g.Genres.Select(x => new { g.Id, x.Slug })).ToListAsync();
            _ = await db.Games.AsNoTracking().Where(g => ids.Contains(g.Id)).SelectMany(g => g.Tags.Select(x => new { g.Id, x.Slug })).ToListAsync();
        });

        // ---- Whole-page costs ----
        await Measure("home page data, SHIPPED", async db => await new EfGameRepository(db).GetHomeAsync(8));
        await Measure("game details data, SHIPPED", async db => await new EfGameRepository(db).GetBySlugAsync("synthetic-game-0100"));

        Console.WriteLine($"{"variant",-80} {"median",8} {"p95",8} {"queries",8} {"reads",9}");
        Console.WriteLine(new string('-', 117));
        foreach (var r in Rows) Console.WriteLine($"{r.Name,-80} {r.MedianMs,8:F1} {r.P95Ms,8:F1} {r.Queries,8} {r.Reads,9}");
    }
}
