using System.Net;
using System.Text;
using GameVault.Application.Metadata;
using GameVault.Infrastructure.Rawg;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Rawg;

internal sealed class FakeUsageStore : IApiUsageStore
{
    public int Total { get; set; }
    public int Increments { get; private set; }
    public Task<int> GetMonthTotalAsync(string provider, DateOnly d, CancellationToken ct = default) => Task.FromResult(Total);
    public Task IncrementAsync(string provider, DateOnly d, CancellationToken ct = default)
    {
        Total++;
        Increments++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _respond;
    public int Calls;
    public List<string> Urls { get; } = new();

    public FakeHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Calls);
        lock (Urls) Urls.Add(request.RequestUri!.PathAndQuery);
        return _respond(request, n, ct);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public class RawgProviderTests
{
    private const string ListJson = """
    {"count":2,"next":null,"results":[
      {"id":3498,"slug":"grand-theft-auto-v","name":"Grand Theft Auto V","released":"2013-09-17",
       "background_image":"https://img/gta.jpg",
       "genres":[{"name":"Action","slug":"action"}],
       "platforms":[{"platform":{"name":"PC","slug":"pc"}},{"platform":{"name":"Xbox One","slug":"xbox-one"}}],
       "tags":[{"name":"Singleplayer","slug":"singleplayer"}],
       "short_screenshots":[{"id":1,"image":"https://img/1.jpg"}]},
      {"id":9,"slug":"x","name":"","released":"not-a-date"}
    ]}
    """;

    private const string DetailJson = """
    {"id":3498,"slug":"grand-theft-auto-v","name":"Grand Theft Auto V","released":"2013-09-17",
     "description_raw":"  Open world crime.  ","background_image":"https://img/gta.jpg",
     "genres":[{"name":"Action","slug":"action"}],"platforms":[],"tags":[]}
    """;

    private static (IGameMetadataProvider Provider, FakeHandler Handler, FakeUsageStore Usage) Build(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond,
        Dictionary<string, string?>? extra = null, int usageTotal = 0)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Rawg:Enabled"] = "true",
            ["Rawg:ApiKey"] = "secret-key",
            ["Rawg:RetryBaseDelayMs"] = "1",
            ["Rawg:AttemptTimeoutSeconds"] = "10",
        };
        foreach (var (k, v) in extra ?? new()) settings[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var usage = new FakeUsageStore { Total = usageTotal };
        var handler = new FakeHandler(respond);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiUsageStore>(usage);
        services.AddRawgIntegration(config);
        services.AddHttpClient<RawgGameProvider>().ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider().GetRequiredService<IGameMetadataProvider>();
        return (provider, handler, usage);
    }

    private static Task<HttpResponseMessage> Ok(string body) => Task.FromResult(FakeHandler.Json(body));

    [Fact]
    public async Task Search_maps_rows_and_skips_rows_without_a_name()
    {
        var (p, h, _) = Build((_, _, _) => Ok(ListJson));

        var games = await p.SearchAsync(null, 2, 40);

        var g = Assert.Single(games);
        Assert.Equal("3498", g.ExternalId);
        Assert.Equal("Grand Theft Auto V", g.Title);
        Assert.Equal(new DateOnly(2013, 9, 17), g.ReleaseDate);
        Assert.Equal(new[] { "pc", "xbox-one" }, g.Platforms.Select(x => x.Slug));
        Assert.Equal("action", Assert.Single(g.Genres).Slug);
        Assert.Single(g.Screenshots);
        Assert.False(g.IsDetailed);
        Assert.Null(g.Description);
        Assert.Contains("page=2", h.Urls[0]);
        Assert.Contains("page_size=40", h.Urls[0]);
        Assert.Contains("key=secret-key", h.Urls[0]);
    }

    [Fact]
    public async Task Details_combine_description_and_screenshots()
    {
        var (p, _, _) = Build((req, _, _) => req.RequestUri!.AbsolutePath.EndsWith("/screenshots")
            ? Ok("""{"results":[{"image":"https://img/a.jpg"},{"image":"https://img/b.jpg"}]}""")
            : Ok(DetailJson));

        var g = await p.GetByExternalIdAsync("3498");

        Assert.NotNull(g);
        Assert.True(g!.IsDetailed);
        Assert.Equal("Open world crime.", g.Description);
        Assert.Equal(2, g.Screenshots.Count);
    }

    [Fact]
    public async Task Unknown_game_returns_null()
    {
        var (p, h, _) = Build((_, _, _) => Task.FromResult(FakeHandler.Json("{}", HttpStatusCode.NotFound)));
        Assert.Null(await p.GetByExternalIdAsync("0"));
        Assert.Equal(1, h.Calls); // a 404 is an answer, not a failure to retry
    }

    [Fact]
    public async Task Retries_on_429_and_5xx_then_succeeds_and_counts_every_attempt()
    {
        var (p, h, usage) = Build((_, n, _) => Task.FromResult(n switch
        {
            1 => FakeHandler.Json("{}", HttpStatusCode.TooManyRequests),
            2 => FakeHandler.Json("{}", HttpStatusCode.BadGateway),
            _ => FakeHandler.Json(ListJson)
        }));

        var games = await p.SearchAsync(null, 1, 40);

        Assert.Single(games);
        Assert.Equal(3, h.Calls);
        Assert.Equal(3, usage.Increments);
    }

    [Fact]
    public async Task Gives_up_after_three_retries()
    {
        var (p, h, _) = Build((_, _, _) => Task.FromResult(FakeHandler.Json("{}", HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAnyAsync<Exception>(() => p.SearchAsync(null, 1, 40));

        Assert.Equal(4, h.Calls);
    }

    [Fact]
    public async Task A_hung_attempt_times_out_and_is_retried()
    {
        var (p, h, _) = Build(async (_, n, ct) =>
        {
            if (n == 1) await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return FakeHandler.Json(ListJson);
        }, new() { ["Rawg:AttemptTimeoutSeconds"] = "0.3" });

        Assert.Single(await p.SearchAsync(null, 1, 40));
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Exhausted_budget_blocks_the_call_without_touching_the_network()
    {
        var (p, h, _) = Build((_, _, _) => Ok(ListJson), usageTotal: 20_000);

        await Assert.ThrowsAsync<BudgetExceededException>(() => p.SearchAsync(null, 1, 40));

        Assert.Equal(0, h.Calls);
    }
}
