using GameVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

/// <summary>
/// Runs the real app against a throwaway database on the local SQL Server (Docker is not required).
/// Set GAMEVAULT_TEST_SERVER to point at another instance, for example in CI.
/// </summary>
public class GameVaultFactory : WebApplicationFactory<Program>
{
    private static readonly string SqlServer = Environment.GetEnvironmentVariable("GAMEVAULT_TEST_SERVER") ?? @".\SQLEXPRESS";
    private readonly string _db = "GameVault_Test_" + Guid.NewGuid().ToString("N");

    private string ConnectionString => $"Server={SqlServer};Database={_db};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>();

    /// <summary>Each factory writes uploads to its own temp folder, never to the real App_Data.</summary>
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), "gv_uploads_" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting, not ConfigureAppConfiguration: Program.cs reads the connection string while building,
        // before late configuration sources are applied, so the latter silently hit the real dev database.
        builder.UseSetting("ConnectionStrings:GameVault", ConnectionString);
        builder.UseSetting("Uploads:Path", UploadsPath);
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        if (Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
        using var cn = new SqlConnection($"Server={SqlServer};Database=master;Trusted_Connection=True;TrustServerCertificate=True");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_db}') IS NOT NULL BEGIN ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}]; END";
        cmd.ExecuteNonQuery();
    }
}

public class CatalogueIntegrationTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    private readonly HttpClient _client;

    public CatalogueIntegrationTests(GameVaultFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Seed_data_has_at_least_30_games_with_the_studio_game()
    {
        await _client.GetAsync("/");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        Assert.True(db.Games.Count() >= 30);
        Assert.Single(db.Games.Where(g => g.IsStudioGame && g.Slug == "untangle"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/games")]
    [InlineData("/games/untangle")]
    [InlineData("/health")]
    public async Task Public_pages_return_ok(string url)
        => Assert.Equal(System.Net.HttpStatusCode.OK, (await _client.GetAsync(url)).StatusCode);

    [Fact]
    public async Task Unknown_game_is_404()
        => Assert.Equal(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync("/games/does-not-exist")).StatusCode);

    [Fact]
    public async Task Genre_filter_only_returns_that_genre()
    {
        var html = await _client.GetStringAsync("/games?genre=racing&pageSize=24");
        Assert.Contains("Forza Horizon 5", html);
        Assert.Contains("Rocket League", html);
        Assert.DoesNotContain("The Witcher 3", html);
    }

    [Fact]
    public async Task Search_matches_title_and_is_safe_with_wildcards()
    {
        Assert.Contains("Dark Souls III", await _client.GetStringAsync("/games?search=souls"));
        var html = await _client.GetStringAsync("/games?search=%25_%5B");
        Assert.Contains("No games match", html);
    }

    [Fact]
    public async Task Page_size_is_capped_at_24()
    {
        var html = await _client.GetStringAsync("/games?pageSize=5000");
        var cards = System.Text.RegularExpressions.Regex.Matches(html, "gv-card").Count;
        Assert.InRange(cards, 1, 24);
    }

    [Fact]
    public async Task Paging_moves_to_a_different_set_of_games()
    {
        var p1 = await _client.GetStringAsync("/games?sort=Title&page=1");
        var p2 = await _client.GetStringAsync("/games?sort=Title&page=2");
        Assert.Contains("Alto&#x27;s Odyssey", p1);
        Assert.DoesNotContain("Alto&#x27;s Odyssey", p2);
    }

    [Fact]
    public async Task Similar_games_appear_on_a_game_page()
    {
        var html = await _client.GetStringAsync("/games/monument-valley");
        Assert.Contains("Similar games", html);
    }
}
