using System.Net;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public class StudioAccessTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public StudioAccessTests(GameVaultFactory factory) => _factory = factory;

    public static TheoryData<string> StudioRoutes => new()
    {
        "/studio", "/studio/games", "/studio/games/new", "/studio/inbox", "/studio/keys", "/studio/inbox/export.csv"
    };

    [Theory]
    [MemberData(nameof(StudioRoutes))]
    public async Task Anonymous_visitors_are_sent_to_sign_in(string url)
    {
        var r = await Web.NewClient(_factory).GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/login", r.Headers.Location!.ToString());
    }

    [Theory]
    [MemberData(nameof(StudioRoutes))]
    public async Task Members_are_denied(string url)
    {
        var r = await (await Web.MemberAsync(_factory)).GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/denied", r.Headers.Location!.ToString());
    }

    [Theory]
    [MemberData(nameof(StudioRoutes))]
    public async Task Moderators_are_denied_because_the_studio_belongs_to_the_owner(string url)
    {
        var r = await (await Web.InRoleAsync(_factory, Roles.Moderator)).GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/denied", r.Headers.Location!.ToString());
    }

    [Theory]
    [MemberData(nameof(StudioRoutes))]
    public async Task Admins_can_open_every_studio_page(string url)
        => Assert.Equal(HttpStatusCode.OK, (await (await Web.InRoleAsync(_factory, Roles.Admin)).GetAsync(url)).StatusCode);

    [Fact]
    public async Task The_studio_link_is_shown_to_the_admin_only()
    {
        Assert.DoesNotContain("href=\"/studio\"", await (await Web.MemberAsync(_factory)).GetStringAsync("/"));
        Assert.DoesNotContain("href=\"/studio\"", await (await Web.InRoleAsync(_factory, Roles.Moderator)).GetStringAsync("/"));
        Assert.Contains("href=\"/studio\"", await (await Web.InRoleAsync(_factory, Roles.Admin)).GetStringAsync("/"));
    }

    [Theory]
    [InlineData("/studio/games/new")]
    [InlineData("/studio/keys/create")]
    [InlineData("/studio/inbox/1/status")]
    public async Task Studio_posts_by_members_are_denied_before_anything_happens(string url)
    {
        var member = await Web.MemberAsync(_factory);
        var r = await Web.PostAsync(member, url, new() { ["Title"] = "x", ["gameId"] = "1", ["status"] = "Read" }, "/account/profile");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/denied", r.Headers.Location!.ToString());
    }
}

public sealed class StudioGamesTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public StudioGamesTests(GameVaultFactory factory) => _factory = factory;

    private static Dictionary<string, string> Form(string title, string? description = "A test game", string? cover = null)
    {
        var d = new Dictionary<string, string> { ["Title"] = title, ["Description"] = description ?? "" };
        if (cover is not null) d["CoverUrl"] = cover;
        return d;
    }

    private async Task<(HttpClient Admin, int Id)> CreateAsync(string title, Dictionary<string, string>? form = null)
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var r = await Web.PostAsync(admin, "/studio/games/new", form ?? Form(title), "/studio/games/new");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return (admin, int.Parse(r.Headers.Location!.OriginalString.Split('/').Last()));
    }

    private int UploadCount => Directory.Exists(_factory.UploadsPath) ? Directory.GetFiles(_factory.UploadsPath).Length : 0;

    [Fact]
    public async Task A_created_game_is_a_studio_game_with_a_generated_slug_and_shows_up_on_the_site()
    {
        var (admin, _) = await CreateAsync("Quest For Slugs", new() { ["Title"] = "Quest For Slugs", ["Description"] = "Hand made", ["Genres"] = "puzzle", ["Tags"] = "casual" });

        var page = await admin.GetStringAsync("/games/quest-for-slugs");
        Assert.Contains("Studio game", page);
        Assert.Contains("Hand made", page);
        Assert.Contains("href=\"/games?genre=puzzle\"", page);
        Assert.Contains("Suggestion board", page);
        Assert.Contains("Quest For Slugs", await admin.GetStringAsync("/")); // featured, so the cache was cleared
        Assert.DoesNotContain("Metadata from", page); // the studio's own game carries no RAWG credit line
    }

    [Fact]
    public async Task Editing_the_title_never_changes_the_address()
    {
        var (admin, id) = await CreateAsync("Original Name");

        var r = await Web.PostAsync(admin, $"/studio/games/{id}", Form("Completely Different"), $"/studio/games/{id}");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/games/original-name")).StatusCode);
        Assert.Contains("Completely Different", await admin.GetStringAsync("/games/original-name"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/games/completely-different")).StatusCode);
    }

    [Fact]
    public async Task A_second_game_with_the_same_title_gets_a_numbered_address()
    {
        await CreateAsync("Twin Title");
        await CreateAsync("Twin Title");

        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/games/twin-title")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/games/twin-title-2")).StatusCode);
    }

    [Fact]
    public async Task A_missing_title_or_a_script_cover_is_refused_and_nothing_is_created()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);

        var noTitle = await Web.PostAsync(admin, "/studio/games/new", Form(""), "/studio/games/new");
        var badCover = await Web.PostAsync(admin, "/studio/games/new", Form("Evil Cover Game", cover: "javascript:alert(1)"), "/studio/games/new");

        Assert.Equal(HttpStatusCode.OK, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, badCover.StatusCode);
        Assert.Contains("cover must be", await badCover.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/games/evil-cover-game")).StatusCode);
    }

    [Fact]
    public async Task Only_studio_games_can_be_edited_here()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var hadesId = await Web.GameIdAsync(_factory, "hades");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/studio/games/{hadesId}")).StatusCode);
        var r = await Web.PostAsync(admin, $"/studio/games/{hadesId}", Form("Hijacked"), "/studio/games/new");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task A_screenshot_upload_is_stored_served_safely_and_shown_on_the_game_page()
    {
        var (admin, id) = await CreateAsync("Shot Test Game");
        var before = UploadCount;

        var r = await Web.UploadAsync(admin, $"/studio/games/{id}/screenshots", Web.Png(), "../../evil.php", $"/studio/games/{id}");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(before + 1, UploadCount);
        var page = await admin.GetStringAsync("/games/shot-test-game");
        var url = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/uploads/[0-9a-f]{32}\\.png)\"").Groups[1].Value;
        Assert.NotEmpty(url); // a generated name and the extension from the content, never the uploaded file name
        var served = await Web.NewClient(_factory).GetAsync(url); // anonymous visitors can see it
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("<?php system($_GET['c']); ?>", "shell.png")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>", "image.svg")]
    [InlineData("<html><script>alert(1)</script></html>", "page.png")]
    [InlineData("GIF89a....", "anim.gif")]
    public async Task Files_that_are_not_png_jpeg_or_webp_are_refused_whatever_they_are_called(string content, string fileName)
    {
        var (admin, id) = await CreateAsync("Reject Test " + Guid.NewGuid().ToString("N")[..6]);
        var before = UploadCount;

        var r = await Web.UploadAsync(admin, $"/studio/games/{id}/screenshots", System.Text.Encoding.UTF8.GetBytes(content), fileName, $"/studio/games/{id}");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("Only PNG, JPEG and WebP", await admin.GetStringAsync($"/studio/games/{id}"));
        Assert.Equal(before, UploadCount);
    }

    [Fact]
    public async Task An_image_over_2_MB_is_refused()
    {
        var (admin, id) = await CreateAsync("Big Image Game");
        var before = UploadCount;

        await Web.UploadAsync(admin, $"/studio/games/{id}/screenshots", Web.Png(StudioLimit), "big.png", $"/studio/games/{id}");

        Assert.Contains("larger than 2 MB", await admin.GetStringAsync($"/studio/games/{id}"));
        Assert.Equal(before, UploadCount);
    }

    private const int StudioLimit = 2 * 1024 * 1024; // 8 header bytes + this is just over 2 MB

    [Fact]
    public async Task A_game_holds_at_most_eight_screenshots_and_removing_one_deletes_its_file()
    {
        var (admin, id) = await CreateAsync("Eight Shots Game");
        for (var i = 0; i < 8; i++)
            await Web.UploadAsync(admin, $"/studio/games/{id}/screenshots", Web.Png(10 + i), "s.png", $"/studio/games/{id}");
        var afterEight = UploadCount;

        await Web.UploadAsync(admin, $"/studio/games/{id}/screenshots", Web.Png(99), "ninth.png", $"/studio/games/{id}");
        var edit = await admin.GetStringAsync($"/studio/games/{id}");

        Assert.Contains("at most 8", edit);
        Assert.Contains("8 / 8", edit);
        Assert.Equal(afterEight, UploadCount);

        var shotId = System.Text.RegularExpressions.Regex.Match(edit, $"/studio/games/{id}/screenshots/(\\d+)/delete").Groups[1].Value;
        var r = await Web.PostAsync(admin, $"/studio/games/{id}/screenshots/{shotId}/delete", new(), $"/studio/games/{id}");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(afterEight - 1, UploadCount);
        Assert.Contains("7 / 8", await admin.GetStringAsync($"/studio/games/{id}"));
    }

    [Fact]
    public async Task A_new_cover_replaces_the_old_one_and_deletes_its_file()
    {
        var (admin, id) = await CreateAsync("Cover Swap Game");

        await Web.UploadAsync(admin, $"/studio/games/{id}/cover", Web.Png(20), "a.png", $"/studio/games/{id}");
        var first = await UploadCoverUrlAsync(admin, id);
        var countWithFirst = UploadCount;
        await Web.UploadAsync(admin, $"/studio/games/{id}/cover", Web.Png(30), "b.png", $"/studio/games/{id}");
        var second = await UploadCoverUrlAsync(admin, id);

        Assert.NotEqual(first, second);
        Assert.Equal(countWithFirst, UploadCount); // one in, one out
        Assert.Equal(HttpStatusCode.NotFound, (await Web.NewClient(_factory).GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Web.NewClient(_factory).GetAsync(second)).StatusCode);
    }

    [Fact]
    public async Task Uploading_without_choosing_a_file_is_handled()
    {
        var (admin, id) = await CreateAsync("No File Game");
        var content = new MultipartFormDataContent { { new StringContent(await Web.TokenAsync(admin, $"/studio/games/{id}")), "__RequestVerificationToken" } };

        var r = await admin.PostAsync($"/studio/games/{id}/screenshots", content);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("Choose an image", await admin.GetStringAsync($"/studio/games/{id}"));
    }

    private static async Task<string> UploadCoverUrlAsync(HttpClient admin, int id)
        => System.Text.RegularExpressions.Regex.Match(await admin.GetStringAsync($"/studio/games/{id}"), "name=\"Form.CoverUrl\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;
}
