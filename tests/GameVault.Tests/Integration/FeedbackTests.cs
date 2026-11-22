using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Application.Feedback;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

internal static class ApiTestSupport
{
    public static async Task<(int KeyId, string Plaintext)> CreateKeyAsync(GameVaultFactory f, HttpClient admin, string slug = "untangle")
    {
        var gameId = await Web.GameIdAsync(f, slug);
        var r = await Web.PostAsync(admin, "/studio/keys/create", new() { ["gameId"] = gameId.ToString() }, "/studio/keys");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        var key = Regex.Match(html, "value=\"(gv_[A-Za-z0-9_-]+)\"").Groups[1].Value;
        Assert.NotEmpty(key);

        var prefix = key[..10];
        using var scope = f.Services.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().ApiKeys.Single(k => k.KeyPrefix == prefix).Id;
        return (id, key);
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient c, string? key, string json, string contentType = "application/json")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/feedback") { Content = new StringContent(json, Encoding.UTF8, contentType) };
        if (key is not null) req.Headers.Add("X-Api-Key", key);
        return c.SendAsync(req);
    }

    public static string Json(string category = "Bug", string body = "It crashed on level 12", string? version = "1.4.0", string? player = "anon-7f3a")
        => JsonSerializer.Serialize(new { category, body, appVersion = version, playerRef = player });
}

public class FeedbackApiTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public FeedbackApiTests(GameVaultFactory factory) => _factory = factory;

    private async Task<(HttpClient Admin, HttpClient Api, int KeyId, string Key)> SetUpAsync(string slug = "untangle")
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var (id, key) = await ApiTestSupport.CreateKeyAsync(_factory, admin, slug);
        return (admin, _factory.CreateClient(), id, key);
    }

    [Fact]
    public async Task A_build_with_a_valid_key_can_post_feedback_and_it_lands_in_the_admin_inbox()
    {
        var (admin, api, _, key) = await SetUpAsync();

        var r = await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json(body: "api-inbox-marker crashed", version: "9.9.9"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var row = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().FeedbackMessages.Single(m => m.Id == id);
        Assert.Equal(FeedbackSource.Api, row.Source);
        Assert.Equal(FeedbackCategory.Bug, row.Category);
        Assert.Equal("9.9.9", row.AppVersion);
        Assert.Equal("anon-7f3a", row.PlayerRef);
        Assert.Equal(FeedbackStatus.New, row.Status);

        var inbox = await admin.GetStringAsync("/studio/inbox?source=Api");
        Assert.Contains("api-inbox-marker crashed", inbox);
        Assert.Contains("v9.9.9", inbox);
    }

    [Fact]
    public async Task The_key_decides_the_game_not_the_request()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await Web.PostAsync(admin, "/studio/games/new", new() { ["Title"] = "Second Studio Game" }, "/studio/games/new");
        var (_, keyB) = await ApiTestSupport.CreateKeyAsync(_factory, admin, "second-studio-game");
        var api = _factory.CreateClient();

        var r = await ApiTestSupport.PostAsync(api, keyB, ApiTestSupport.Json(body: "belongs-to-second-game"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var row = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().FeedbackMessages.Include(m => m.Game).Single(m => m.Body == "belongs-to-second-game");
        Assert.Equal("second-studio-game", row.Game!.Slug);
    }

    [Fact]
    public async Task Requests_without_a_key_or_with_a_wrong_one_get_a_401_problem_response()
    {
        var api = _factory.CreateClient();

        var missing = await ApiTestSupport.PostAsync(api, null, ApiTestSupport.Json());
        var unknown = await ApiTestSupport.PostAsync(api, ApiKeyServiceKey(), ApiTestSupport.Json());
        var malformed = await ApiTestSupport.PostAsync(api, "not-a-key", ApiTestSupport.Json());

        foreach (var r in new[] { missing, unknown, malformed })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
            Assert.Equal(401, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetInt32());
        }
        Assert.Contains("ApiKey", missing.Headers.WwwAuthenticate.ToString());
    }

    private static string ApiKeyServiceKey() => GameVault.Application.Studio.ApiKeyService.Generate();

    [Fact]
    public async Task A_revoked_key_is_rejected_straight_away()
    {
        var (admin, api, keyId, key) = await SetUpAsync();
        Assert.Equal(HttpStatusCode.Created, (await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json())).StatusCode);

        var revoke = await Web.PostAsync(admin, $"/studio/keys/{keyId}/revoke", new(), "/studio/keys");
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json())).StatusCode);
        Assert.Contains("Revoked", await admin.GetStringAsync("/studio/keys"));
    }

    [Fact]
    public async Task A_signed_in_admin_cookie_is_not_an_api_credential()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var r = await ApiTestSupport.PostAsync(admin, null, ApiTestSupport.Json());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task The_endpoint_needs_no_anti_forgery_token_or_cookie()
    {
        var (_, _, _, key) = await SetUpAsync();
        var bare = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        Assert.Equal(HttpStatusCode.Created, (await ApiTestSupport.PostAsync(bare, key, ApiTestSupport.Json())).StatusCode);
    }

    [Fact]
    public async Task Invalid_fields_come_back_as_a_400_with_every_problem_listed()
    {
        var (_, api, _, key) = await SetUpAsync();

        var r = await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json(category: "Complaint", body: "   ", version: "1.0; DROP TABLE"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
        var errors = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("category", out _));
        Assert.True(errors.TryGetProperty("body", out _));
        Assert.True(errors.TryGetProperty("appVersion", out _));
    }

    [Fact]
    public async Task A_body_over_the_limit_is_rejected_and_not_stored()
    {
        var (_, api, _, key) = await SetUpAsync();
        var r = await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json(body: new string('x', FeedbackService.MaxBody + 1)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task A_request_over_16_KB_is_refused_with_413()
    {
        var (_, api, _, key) = await SetUpAsync();
        var r = await ApiTestSupport.PostAsync(api, key, ApiTestSupport.Json(body: new string('x', 20_000)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public async Task Malformed_json_is_a_400(string json)
    {
        var (_, api, _, key) = await SetUpAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestSupport.PostAsync(api, key, json)).StatusCode);
    }

    [Fact]
    public async Task Only_json_is_accepted()
    {
        var (_, api, _, key) = await SetUpAsync();
        var r = await ApiTestSupport.PostAsync(api, key, "category=Bug&body=x", "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
    }

    [Fact]
    public async Task The_key_is_shown_once_and_only_its_hash_is_stored()
    {
        var (admin, _, _, key) = await SetUpAsync();
        var prefix = key[..10];

        using (var scope = _factory.Services.CreateScope())
        {
            var row = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().ApiKeys.Single(k => k.KeyPrefix == prefix);
            Assert.NotEqual(key, row.KeyHash);
            Assert.Equal(GameVault.Application.Studio.KeyHasher.Hash(key), row.KeyHash);
            Assert.DoesNotContain(key, row.KeyHash + row.KeyPrefix);
        }

        var list = await admin.GetStringAsync("/studio/keys");
        Assert.DoesNotContain(key, list);
        Assert.Contains(key[..10], list);
    }

    [Fact]
    public async Task Creating_the_key_response_is_not_cacheable()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var gameId = await Web.GameIdAsync(_factory, "untangle");

        var r = await Web.PostAsync(admin, "/studio/keys/create", new() { ["gameId"] = gameId.ToString() }, "/studio/keys");

        Assert.True(r.Headers.CacheControl!.NoStore); // anti-forgery adds no-cache beside it
    }

    [Fact]
    public async Task Keys_cannot_be_created_for_a_game_that_is_not_the_studios()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var hades = await Web.GameIdAsync(_factory, "hades");

        var r = await Web.PostAsync(admin, "/studio/keys/create", new() { ["gameId"] = hades.ToString() }, "/studio/keys");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("only be created for studio games", await admin.GetStringAsync("/studio/keys"));
    }
}

public sealed class RateLimitFactory : GameVaultFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        ["FeedbackApi:PermitPerMinute"] = "3",
        ["FeedbackApi:IpPermitPerMinute"] = "20"
    };
}

public class FeedbackRateLimitTests : IClassFixture<RateLimitFactory>
{
    private readonly RateLimitFactory _factory;
    public FeedbackRateLimitTests(RateLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_key_has_its_own_limit_and_an_address_cap_stops_made_up_keys()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var (_, keyA) = await ApiTestSupport.CreateKeyAsync(_factory, admin);
        var (_, keyB) = await ApiTestSupport.CreateKeyAsync(_factory, admin);
        var api = _factory.CreateClient();

        // Key A: three allowed, the fourth is limited.
        var a = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) a.Add((await ApiTestSupport.PostAsync(api, keyA, ApiTestSupport.Json(body: $"limit-{i}"))).StatusCode);
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, a);

        // Key B is unaffected by A's limit.
        Assert.Equal(HttpStatusCode.Created, (await ApiTestSupport.PostAsync(api, keyB, ApiTestSupport.Json())).StatusCode);

        // The limited reply is a problem document with a Retry-After hint.
        var limited = await ApiTestSupport.PostAsync(api, keyA, ApiTestSupport.Json());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
        Assert.True(limited.Headers.Contains("Retry-After"));

        // Fresh made-up keys each get a new per-key allowance, so only the address cap can stop this flood.
        // (ASP.NET charges a request that an endpoint policy rejects two address permits, so the exact count is not asserted.)
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 30; i++)
            statuses.Add((await ApiTestSupport.PostAsync(api, GameVault.Application.Studio.ApiKeyService.Generate(), ApiTestSupport.Json())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.True(statuses.IndexOf(HttpStatusCode.TooManyRequests) <= 14, "the address cap should have applied well before 30 requests");
        Assert.All(statuses.Skip(statuses.IndexOf(HttpStatusCode.TooManyRequests)), s => Assert.Equal(HttpStatusCode.TooManyRequests, s));
    }

    [Fact]
    public async Task The_rest_of_the_site_is_not_rate_limited()
    {
        var c = Web.NewClient(_factory);
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/games")).StatusCode);
    }
}

public class FeedbackWebAndInboxTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public FeedbackWebAndInboxTests(GameVaultFactory factory) => _factory = factory;

    private async Task SeedAsync(FeedbackCategory category, string body, string? version = null, FeedbackSource source = FeedbackSource.Api)
    {
        using var scope = _factory.Services.CreateScope();
        var feedback = scope.ServiceProvider.GetRequiredService<FeedbackService>();
        var id = await Web.GameIdAsync(_factory, "untangle");
        var r = await feedback.SubmitAsync(new FeedbackSubmission(id, source, category.ToString(), body, null, version));
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task A_member_can_send_feedback_from_the_game_page_and_it_reaches_the_inbox_as_web()
    {
        var member = await Web.MemberAsync(_factory, "Web Sender");
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);

        var r = await Web.PostAsync(member, "/games/untangle/feedback", new() { ["Category"] = "Praise", ["Message"] = "web-form-marker love it" }, "/games/untangle/feedback");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var inbox = await admin.GetStringAsync("/studio/inbox?source=Web");
        Assert.Contains("web-form-marker love it", inbox);
        Assert.Contains("Web Sender", inbox);
    }

    [Fact]
    public async Task The_feedback_form_needs_sign_in_and_a_studio_game()
    {
        var anon = await Web.NewClient(_factory).GetAsync("/games/untangle/feedback");
        Assert.Contains("/account/login", anon.Headers.Location!.ToString());

        var member = await Web.MemberAsync(_factory);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/games/hades/feedback")).StatusCode);
        var post = await Web.PostAsync(member, "/games/hades/feedback", new() { ["Category"] = "Bug", ["Message"] = "x" }, "/games/untangle/feedback");
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task An_empty_or_invalid_web_message_is_refused()
    {
        var member = await Web.MemberAsync(_factory);

        var empty = await Web.PostAsync(member, "/games/untangle/feedback", new() { ["Category"] = "Bug", ["Message"] = "  " }, "/games/untangle/feedback");
        var badCategory = await Web.PostAsync(member, "/games/untangle/feedback", new() { ["Category"] = "99", ["Message"] = "hello" }, "/games/untangle/feedback");

        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, badCategory.StatusCode);
    }

    [Fact]
    public async Task The_inbox_filters_by_category_status_and_source()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await SeedAsync(FeedbackCategory.Bug, "filter-bug-marker");
        await SeedAsync(FeedbackCategory.Idea, "filter-idea-marker");
        await SeedAsync(FeedbackCategory.Praise, "filter-praise-marker", source: FeedbackSource.Web);

        var bugs = await admin.GetStringAsync("/studio/inbox?category=Bug");
        Assert.Contains("filter-bug-marker", bugs);
        Assert.DoesNotContain("filter-idea-marker", bugs);

        var web = await admin.GetStringAsync("/studio/inbox?source=Web");
        Assert.Contains("filter-praise-marker", web);
        Assert.DoesNotContain("filter-bug-marker", web);

        Assert.Contains("filter-idea-marker", await admin.GetStringAsync("/studio/inbox?status=New"));
        Assert.DoesNotContain("filter-idea-marker", await admin.GetStringAsync("/studio/inbox?status=Archived"));
    }

    [Fact]
    public async Task Status_changes_move_a_message_between_filters_and_unknown_statuses_are_refused()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await SeedAsync(FeedbackCategory.Bug, "status-change-marker");
        int id;
        using (var scope = _factory.Services.CreateScope())
            id = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().FeedbackMessages.Single(m => m.Body == "status-change-marker").Id;

        var r = await Web.PostAsync(admin, $"/studio/inbox/{id}/status", new() { ["status"] = "Archived", ["returnUrl"] = "/studio/inbox?category=Bug" }, "/studio/inbox");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/studio/inbox?category=Bug", r.Headers.Location!.OriginalString);
        Assert.Contains("status-change-marker", await admin.GetStringAsync("/studio/inbox?status=Archived"));
        Assert.DoesNotContain("status-change-marker", await admin.GetStringAsync("/studio/inbox?status=New"));

        var bad = await Web.PostAsync(admin, $"/studio/inbox/{id}/status", new() { ["status"] = "Banana" }, "/studio/inbox");
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        var offsite = await Web.PostAsync(admin, $"/studio/inbox/{id}/status", new() { ["status"] = "Read", ["returnUrl"] = "https://evil.example" }, "/studio/inbox");
        Assert.DoesNotContain("evil.example", offsite.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_inbox_pages_through_long_lists()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        for (var i = 0; i < 27; i++) await SeedAsync(FeedbackCategory.Idea, $"paging-marker-{i:00}");

        var first = await admin.GetStringAsync("/studio/inbox?category=Idea");
        Assert.Contains("Page 1 of", first);
        Assert.Contains("Next", first);
        var second = await admin.GetStringAsync("/studio/inbox?category=Idea&page=2");
        Assert.Contains("Page 2 of", second);
    }

    [Fact]
    public async Task The_csv_export_is_excel_safe_and_respects_filters()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await SeedAsync(FeedbackCategory.Bug, "=HYPERLINK(\"http://evil.example\",\"click\")");
        await SeedAsync(FeedbackCategory.Bug, "csv, with \"quotes\"\nand a newline");
        await SeedAsync(FeedbackCategory.Praise, "csv-praise-marker");

        var r = await admin.GetAsync("/studio/inbox/export.csv?category=Bug");
        var bytes = await r.Content.ReadAsByteArrayAsync();
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("gamevault-feedback-", r.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Contains("Id,Game,Source,Category,Status,AppVersion,PlayerRef,CreatedUtc,Body", csv);
        Assert.Contains("'=HYPERLINK(", csv);                  // neutralised formula
        Assert.DoesNotContain(",=HYPERLINK(", csv);
        Assert.Contains("\"csv, with \"\"quotes\"\"\nand a newline\"", csv);
        Assert.DoesNotContain("csv-praise-marker", csv);        // filter applied
    }

    [Fact]
    public async Task The_dashboard_counts_feedback_per_version_and_new_messages_per_game()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await SeedAsync(FeedbackCategory.Bug, "dash-1", version: "7.7.7");
        await SeedAsync(FeedbackCategory.Bug, "dash-2", version: "7.7.7");
        await SeedAsync(FeedbackCategory.Praise, "dash-3", version: "7.7.7");

        var page = await admin.GetStringAsync("/studio");

        Assert.Contains("Untangle", page);
        var row = Regex.Match(page, @"<code>7\.7\.7</code></td><td class=""text-end"">(\d+)</td><td class=""text-end"">(\d+)</td><td class=""text-end"">(\d+)</td>");
        Assert.True(row.Success, "version row not found");
        Assert.Equal(new[] { "2", "0", "1" }, new[] { row.Groups[1].Value, row.Groups[2].Value, row.Groups[3].Value });
        Assert.Contains("Newest feedback", page);
    }
}
