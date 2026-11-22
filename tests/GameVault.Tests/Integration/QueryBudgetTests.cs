using System.Data.Common;
using System.Net;
using GameVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace GameVault.Tests.Integration;

/// <summary>Counts every SQL command the app sends, so a page that quietly starts running one query per row fails a test.</summary>
public sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r) { Interlocked.Increment(ref _count); return r; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Interlocked.Increment(ref _count); return ValueTask.FromResult(r); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r) { Interlocked.Increment(ref _count); return r; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Interlocked.Increment(ref _count); return ValueTask.FromResult(r); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r) { Interlocked.Increment(ref _count); return r; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default) { Interlocked.Increment(ref _count); return ValueTask.FromResult(r); }
}

public sealed class CountingFactory : GameVaultFactory
{
    public CommandCountingInterceptor Counter { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<GameVaultDbContext>(o => o.AddInterceptors(Counter)));
    }
}

public class QueryBudgetTests : IClassFixture<CountingFactory>
{
    private readonly CountingFactory _factory;
    private readonly ITestOutputHelper _out;

    public QueryBudgetTests(CountingFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _ = factory.CreateClient(); // boots the host so migration and seeding are not counted
    }

    private async Task<int> CountAsync(HttpClient client, string url)
    {
        _factory.Counter.Reset();
        var r = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var n = _factory.Counter.Count;
        _out.WriteLine($"{url,-45} {n} queries");
        return n;
    }

    // Upper bounds sit just above what the pages need today. They are not a target to creep up to: a number that grows with the
    // page size or the row count means an N+1 has crept in. Measured values and the reasoning are in docs/PERFORMANCE.md.

    [Fact]
    public async Task The_home_page_needs_three_queries_cold_and_none_warm()
    {
        var c = Web.NewClient(_factory);
        Assert.InRange(await CountAsync(c, "/"), 1, 3);
        Assert.Equal(0, await CountAsync(c, "/"));
    }

    [Fact]
    public async Task The_catalogue_needs_a_count_a_page_and_three_filter_lists_cold_and_none_warm()
    {
        var c = Web.NewClient(_factory);
        Assert.InRange(await CountAsync(c, "/games?genre=puzzle&sort=TopRated"), 1, 5);
        Assert.Equal(0, await CountAsync(c, "/games?genre=puzzle&sort=TopRated"));
    }

    [Fact]
    public async Task A_page_of_the_catalogue_costs_the_same_however_many_games_it_shows()
    {
        var c = Web.NewClient(_factory);
        await CountAsync(c, "/games"); // fills the shared filter lists, which the first catalogue request would otherwise pay for
        var small = await CountAsync(c, "/games?pageSize=1&sort=Title");
        var large = await CountAsync(c, "/games?pageSize=24&sort=Title");
        Assert.Equal(small, large); // one query per row would make these differ
    }

    [Fact]
    public async Task A_game_page_needs_a_fixed_handful_of_queries_cold_and_none_warm_for_a_visitor()
    {
        var c = Web.NewClient(_factory);
        Assert.InRange(await CountAsync(c, "/games/hades"), 1, 13);
        Assert.Equal(0, await CountAsync(c, "/games/hades"));
    }

    [Fact]
    public async Task A_game_page_for_a_signed_in_member_adds_only_their_own_lookups()
    {
        var member = await Web.MemberAsync(_factory);
        await CountAsync(member, "/games/celeste"); // fills the shared cache
        Assert.InRange(await CountAsync(member, "/games/celeste"), 1, 4); // their lists and their review, nothing else
    }

    [Fact]
    public async Task Reviews_on_a_page_do_not_add_a_query_each()
    {
        var a = await Web.MemberAsync(_factory);
        var viewer = await Web.NewClient(_factory).GetAsync("/"); // keep the client warm
        await Web.SaveReviewAsync(a, "terraria", 4, "one");
        var b = await Web.MemberAsync(_factory);
        await Web.SaveReviewAsync(b, "terraria", 5, "two");
        var many = Web.NewClient(_factory);
        var withTwo = await CountAsync(many, "/games/terraria");

        for (var i = 0; i < 4; i++) await Web.SaveReviewAsync(await Web.MemberAsync(_factory), "terraria", 3, $"more {i}");
        var withSix = await CountAsync(Web.NewClient(_factory), "/games/terraria");

        Assert.Equal(withTwo, withSix);
        _ = viewer;
    }

    [Fact]
    public async Task Similar_games_cost_the_same_for_any_game()
    {
        var one = await CountAsync(Web.NewClient(_factory), "/games/portal-2");
        var other = await CountAsync(Web.NewClient(_factory), "/games/doom-eternal");
        Assert.Equal(one, other);
    }

    [Fact]
    public async Task The_studio_inbox_pages_with_a_fixed_number_of_queries()
    {
        var admin = await Web.InRoleAsync(_factory, GameVault.Domain.Roles.Admin);
        var n = await CountAsync(admin, "/studio/inbox");
        Assert.InRange(n, 1, 4);
    }
}
