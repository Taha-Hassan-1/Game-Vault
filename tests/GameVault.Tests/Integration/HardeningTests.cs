using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Application.Catalogue;
using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Domain.Entities;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

internal static class Html
{
    /// <summary>The strict CSP only holds if no page carries inline script, handlers, styles or javascript: links.</summary>
    public static void AssertNoInlineCode(string html, string where)
    {
        Assert.False(Regex.IsMatch(html, @"<script(?![^>]*\bsrc\s*=)[^>]*>", RegexOptions.IgnoreCase), $"inline <script> on {where}");
        Assert.False(Regex.IsMatch(html, @"\son[a-z]+\s*=\s*[""']", RegexOptions.IgnoreCase), $"inline event handler on {where}");
        Assert.False(Regex.IsMatch(html, @"\sstyle\s*=\s*[""']", RegexOptions.IgnoreCase), $"inline style attribute on {where}");
        Assert.False(Regex.IsMatch(html, @"<style[\s>]", RegexOptions.IgnoreCase), $"<style> block on {where}");
        Assert.False(html.Contains("javascript:", StringComparison.OrdinalIgnoreCase), $"javascript: URL on {where}");
    }
}

public class SecurityHeaderTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public SecurityHeaderTests(GameVaultFactory factory) => _factory = factory;

    public static TheoryData<string> Urls => new() { "/", "/games", "/games/hades", "/account/login", "/health", "/does/not/exist", "/api/v1/feedback" };

    [Theory]
    [MemberData(nameof(Urls))]
    public async Task Every_response_carries_the_security_headers(string url)
    {
        var r = await Web.NewClient(_factory).GetAsync(url);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", r.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
    }

    [Fact]
    public async Task Static_files_get_the_headers_too()
    {
        var c = Web.NewClient(_factory);
        var css = Regex.Match(await c.GetStringAsync("/"), "href=\"(/css/site\\.css[^\"]*)\"").Groups[1].Value;
        Assert.NotEmpty(css);

        var r = await c.GetAsync(css);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Versioned_assets_are_cached_for_a_year_and_plain_ones_still_revalidate()
    {
        var c = Web.NewClient(_factory);
        var html = await c.GetStringAsync("/");
        var versioned = Regex.Matches(html, "(?:href|src)=\"(/(?:css|js|lib)/[^\"]+\\?v=[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.True(versioned.Count >= 4, "the layout should reference css, bootstrap css, bootstrap js and site.js with ?v=hash");
        foreach (var url in versioned)
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(SecurityHeaders.ImmutableCache, r.Headers.CacheControl!.ToString().Replace(" ", ""));
            Assert.NotNull(r.Headers.ETag);
        }

        var plain = await c.GetAsync(versioned[0].Split('?')[0]);
        Assert.DoesNotContain("immutable", plain.Headers.CacheControl?.ToString() ?? "");

        // A conditional request for an unchanged versioned file is still answered 304 with the same long lifetime.
        var first = await c.GetAsync(versioned[0]);
        var conditional = new HttpRequestMessage(HttpMethod.Get, versioned[0]);
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await c.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Pages_are_never_marked_long_lived_and_a_missing_versioned_file_is_not()
    {
        var c = Web.NewClient(_factory);
        Assert.DoesNotContain("immutable", (await c.GetAsync("/games")).Headers.CacheControl?.ToString() ?? "");
        var missing = await c.GetAsync("/css/not-there.css?v=abc");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.DoesNotContain("immutable", missing.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public void The_policy_allows_no_inline_or_eval_and_forbids_framing_and_plugins()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy;
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Contains("script-src 'self';", csp);
        Assert.Contains("style-src 'self';", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.DoesNotContain("*", csp.Replace("https:", ""));
    }

    [Fact]
    public async Task No_page_in_the_site_uses_inline_script_handlers_or_styles()
    {
        // Put content on the pages that render it: reviews, suggestions, feedback, an uploaded cover and screenshot.
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var moderator = await Web.InRoleAsync(_factory, Roles.Moderator);
        var member = await Web.MemberAsync(_factory, "Crawl Member");
        await Web.SaveReviewAsync(member, "hades", 4, "A review so the page has one\nwith two lines");
        await Web.PostAsync(member, "/games/untangle/suggestions", new() { ["Title"] = "A suggestion for the crawl", ["Body"] = "details" }, "/games/untangle/suggestions");
        await Web.PostAsync(member, "/games/untangle/feedback", new() { ["Category"] = "Bug", ["Message"] = "feedback for the crawl" }, "/games/untangle/feedback");
        await Web.PostAsync(member, "/games/untangle/lists/Wishlist", new() { ["present"] = "true" }, "/games/untangle");
        var untangle = await Web.GameIdAsync(_factory, "untangle");
        await Web.UploadAsync(admin, $"/studio/games/{untangle}/cover", Web.Png(), "c.png", $"/studio/games/{untangle}");
        await Web.UploadAsync(admin, $"/studio/games/{untangle}/screenshots", Web.Png(), "s.png", $"/studio/games/{untangle}");
        var reviewId = await ReviewIdAsync("hades");
        await Web.PostAsync(moderator, $"/moderation/{reviewId}/hide", new(), "/moderation"); // a hidden review shows in the queue

        var pages = new (HttpClient Client, string Url)[]
        {
            (Web.NewClient(_factory), "/"), (Web.NewClient(_factory), "/games"), (Web.NewClient(_factory), "/games?search=zzzz-no-match"),
            (Web.NewClient(_factory), "/games?genre=puzzle&sort=TopRated"), (Web.NewClient(_factory), "/games/hades"),
            (Web.NewClient(_factory), "/games/untangle"), (Web.NewClient(_factory), "/games/untangle/suggestions"),
            (Web.NewClient(_factory), "/account/login"), (Web.NewClient(_factory), "/account/register"), (Web.NewClient(_factory), "/does/not/exist"),
            (member, "/me/lists"), (member, "/account/profile"), (member, "/games/hades"), (member, "/games/hades/reviews"),
            (member, "/games/untangle"), (member, "/games/untangle/suggestions"), (member, "/games/untangle/feedback"),
            (moderator, "/moderation"), (moderator, "/games/hades"),
            (admin, "/"), (admin, "/games/untangle/suggestions"), (admin, "/studio"), (admin, "/studio/games"), (admin, "/studio/games/new"),
            (admin, $"/studio/games/{untangle}"), (admin, "/studio/inbox"), (admin, "/studio/keys"), (admin, "/account/denied")
        };
        foreach (var (client, url) in pages)
        {
            var r = await client.GetAsync(url);
            Html.AssertNoInlineCode(await r.Content.ReadAsStringAsync(), url);
        }

        // The one-time key page is only reachable by POST.
        var created = await Web.PostAsync(admin, "/studio/keys/create", new() { ["gameId"] = untangle.ToString() }, "/studio/keys");
        Html.AssertNoInlineCode(await created.Content.ReadAsStringAsync(), "/studio/keys/create");
    }

    [Fact]
    public async Task The_one_time_key_field_selects_itself_through_a_data_attribute_not_an_inline_handler()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var gameId = await Web.GameIdAsync(_factory, "untangle");

        var html = await (await Web.PostAsync(admin, "/studio/keys/create", new() { ["gameId"] = gameId.ToString() }, "/studio/keys")).Content.ReadAsStringAsync();

        Assert.Contains("data-select-on-focus", html);
        Assert.DoesNotContain("onfocus", html);
        Assert.Contains("focusin", await Web.NewClient(_factory).GetStringAsync(Regex.Match(await Web.NewClient(_factory).GetStringAsync("/"), "src=\"(/js/site\\.js[^\"]*)\"").Groups[1].Value));
    }

    [Fact]
    public async Task The_sign_in_cookie_is_http_only_and_same_site()
    {
        var c = Web.NewClient(_factory);
        var email = Web.NewEmail();
        var r = await Web.RegisterAsync(c, email);

        var cookies = r.Headers.GetValues("Set-Cookie").Select(x => x.ToLowerInvariant()).ToList();
        var auth = Assert.Single(cookies, x => x.StartsWith(".gamevault.auth="));
        Assert.Contains("httponly", auth);
        Assert.Contains("samesite=lax", auth);
    }

    [Fact]
    public async Task Game_cover_fallbacks_use_a_preset_colour_class_for_every_id()
    {
        for (var id = 0; id < 400; id++)
            Assert.InRange(GameVault.Web.Models.Hue.Index(id), 0, GameVault.Web.Models.Hue.Steps - 1);
        await Task.CompletedTask;
    }

    private async Task<int> ReviewIdAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVault.Infrastructure.Persistence.GameVaultDbContext>();
        return db.Reviews.Where(r => r.Game!.Slug == slug).OrderByDescending(r => r.Id).Select(r => r.Id).First();
    }
}

/// <summary>The real Production pipeline: exception handler, HSTS, secure cookies. Requests go over https so Secure cookies work.</summary>
public class ProductionFactory : GameVaultFactory
{
    protected override string EnvironmentName => "Production";

    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        ["Database:MigrateOnStart"] = "true"
    };

    public HttpClient SecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        // Not localhost: ASP.NET never sends HSTS to loopback names, so a real-looking host is needed to see the header.
        BaseAddress = new Uri("https://gamevault.test")
    });
}

public class ProductionHardeningTests : IClassFixture<ProductionFactory>
{
    private readonly ProductionFactory _factory;
    public ProductionHardeningTests(ProductionFactory factory) => _factory = factory;

    [Fact]
    public async Task Https_responses_carry_a_one_year_hsts_policy()
    {
        var r = await _factory.SecureClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var hsts = r.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=31536000", hsts);
        Assert.Contains("includeSubDomains", hsts);
    }

    [Fact]
    public async Task The_sign_in_and_anti_forgery_cookies_are_secure_http_only_cookies()
    {
        var c = _factory.SecureClient();
        var form = await c.GetAsync("/account/register");
        var antiforgery = Assert.Single(form.Headers.GetValues("Set-Cookie").Select(x => x.ToLowerInvariant()), x => x.StartsWith(".gamevault.antiforgery="));
        Assert.Contains("secure", antiforgery);
        Assert.Contains("httponly", antiforgery);

        var token = await Web.TokenAsync(c, "/account/register");
        var r = await c.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = Web.NewEmail(), ["DisplayName"] = "Prod", ["Password"] = Web.Password, ["ConfirmPassword"] = Web.Password, ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var auth = Assert.Single(r.Headers.GetValues("Set-Cookie").Select(x => x.ToLowerInvariant()), x => x.StartsWith(".gamevault.auth="));
        Assert.Contains("secure", auth);
        Assert.Contains("httponly", auth);
        Assert.Contains("samesite=lax", auth);
    }

    [Fact]
    public async Task An_unknown_page_gets_the_friendly_404_with_the_original_status()
    {
        var r = await _factory.SecureClient().GetAsync("/no/such/page");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("We could not find that page", html);
        Assert.Contains("Back to the home page", html);
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task An_unknown_game_shows_the_friendly_404_page_rather_than_an_empty_response()
    {
        var r = await _factory.SecureClient().GetAsync("/games/no-such-game");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("We could not find that page", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_errors_are_problem_documents_not_html_pages()
    {
        var c = _factory.SecureClient();

        var unknown = await c.GetAsync("/api/v1/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType!.MediaType);

        var wrongMethod = await c.GetAsync("/api/v1/feedback");
        Assert.Equal("application/problem+json", wrongMethod.Content.Headers.ContentType!.MediaType);
        // A GET on the POST-only route is answered as "nothing here", still as a problem document, never an HTML page.
        Assert.True(wrongMethod.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"status was {(int)wrongMethod.StatusCode}");
    }

    [Fact]
    public async Task Health_reports_the_database_and_nothing_secret()
    {
        var r = await _factory.SecureClient().GetAsync("/health");
        var body = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "database" }, checks);
        Assert.DoesNotContain("Server=", body);
        Assert.DoesNotContain("SQLEXPRESS", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rawg_health_is_reported_separately_and_never_fails_the_site()
    {
        var r = await _factory.SecureClient().GetAsync("/health/rawg");
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("rawg", doc.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Contains("switched off", doc.RootElement.GetProperty("checks")[0].GetProperty("description").GetString());
    }
}

public sealed class ThrowingCatalog : IGameCatalog
{
    public const string Secret = "secret-internal-detail-4711";
    private static InvalidOperationException Boom() => new(Secret);

    public Task<PagedResult<GameCard>> SearchAsync(CatalogueQuery query, CancellationToken ct = default) => throw Boom();
    public Task<GameDetails?> GetDetailsAsync(string slug, CancellationToken ct = default) => throw Boom();
    public Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default) => throw Boom();
    public Task<HomeModel> GetHomeAsync(CancellationToken ct = default) => throw Boom();
    public Task<IReadOnlyList<GameCard>> GetSimilarAsync(string slug, int count, CancellationToken ct = default) => throw Boom();
    public ValueTask InvalidateAsync(CancellationToken ct = default) => throw Boom();
}

public sealed class ThrowingApiKeys : IApiKeyRepository
{
    private static InvalidOperationException Boom() => new(ThrowingCatalog.Secret);
    public Task AddAsync(ApiKey key, CancellationToken ct = default) => throw Boom();
    public Task<int?> FindActiveGameIdAsync(string keyHash, CancellationToken ct = default) => throw Boom();
    public Task<IReadOnlyList<ApiKeyRow>> ListAsync(CancellationToken ct = default) => throw Boom();
    public Task<bool> RevokeAsync(int id, DateTime now, CancellationToken ct = default) => throw Boom();
}

/// <summary>Production with the catalogue and the key store broken, to see what a visitor and a Unity build get back.</summary>
public sealed class FaultyProductionFactory : ProductionFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddScoped<IGameCatalog, ThrowingCatalog>();
            s.AddScoped<IApiKeyRepository, ThrowingApiKeys>();
        });
    }
}

public class ErrorHandlingTests : IClassFixture<FaultyProductionFactory>
{
    private readonly FaultyProductionFactory _factory;
    public ErrorHandlingTests(FaultyProductionFactory factory) => _factory = factory;

    [Fact]
    public async Task A_crash_on_the_site_shows_a_friendly_page_with_a_reference_and_no_internals()
    {
        var r = await _factory.SecureClient().GetAsync("/games");
        var html = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("Quote this reference", html);
        Assert.DoesNotContain(ThrowingCatalog.Secret, html);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.DoesNotContain("   at ", html); // no stack trace
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
        Html.AssertNoInlineCode(html, "error page");
    }

    [Fact]
    public async Task A_crash_on_the_api_is_a_problem_document_with_a_trace_id_and_no_internals()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/feedback")
        {
            Content = new StringContent("""{"category":"Bug","body":"x"}""", System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
        };
        req.Headers.Add("X-Api-Key", ApiKeyService.Generate());

        var r = await _factory.SecureClient().SendAsync(req);
        var body = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain(ThrowingCatalog.Secret, body);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("traceId").GetString()));
    }
}
