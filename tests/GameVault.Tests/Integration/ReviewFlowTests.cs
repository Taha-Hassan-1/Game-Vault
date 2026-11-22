using System.Net;
using GameVault.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public class ReviewFlowTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public ReviewFlowTests(GameVaultFactory factory) => _factory = factory;

    [Fact]
    public async Task A_review_is_shown_and_the_game_rating_is_recomputed()
    {
        const string slug = "monument-valley";
        var ada = await Web.MemberAsync(_factory, "Ada");
        var bob = await Web.MemberAsync(_factory, "Bob");

        Assert.Equal(HttpStatusCode.Redirect, (await Web.SaveReviewAsync(ada, slug, 5, "Beautiful puzzles")).StatusCode);
        Assert.Equal((1, 5.0), await Web.RatingAsync(_factory, slug));

        await Web.SaveReviewAsync(bob, slug, 2, "Too short");
        Assert.Equal((2, 3.5), await Web.RatingAsync(_factory, slug));

        var page = await ada.GetStringAsync($"/games/{slug}");
        Assert.Contains("Beautiful puzzles", page);
        Assert.Contains("Too short", page);
        Assert.Contains("3.5", page); // the cached page was invalidated by the review
    }

    [Fact]
    public async Task Saving_again_edits_the_one_review_instead_of_adding_another()
    {
        const string slug = "alto-s-odyssey";
        var c = await Web.MemberAsync(_factory);

        await Web.SaveReviewAsync(c, slug, 1, "Not for me");
        await Web.SaveReviewAsync(c, slug, 5, "Grew on me");

        Assert.Equal((1, 5.0), await Web.RatingAsync(_factory, slug));
        var page = await c.GetStringAsync($"/games/{slug}");
        Assert.Contains("Grew on me", page);
        Assert.DoesNotContain("Not for me", page);
        Assert.Contains("(edited)", page);
    }

    [Fact]
    public async Task Deleting_your_review_removes_it_from_the_rating()
    {
        const string slug = "two-dots";
        var a = await Web.MemberAsync(_factory);
        var b = await Web.MemberAsync(_factory);
        await Web.SaveReviewAsync(a, slug, 5, "x");
        await Web.SaveReviewAsync(b, slug, 3, "y");

        var r = await Web.PostAsync(a, $"/games/{slug}/reviews/delete", new(), $"/games/{slug}/reviews");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal((1, 3.0), await Web.RatingAsync(_factory, slug));

        await Web.PostAsync(b, $"/games/{slug}/reviews/delete", new(), $"/games/{slug}/reviews");
        Assert.Equal((0, 0.0), await Web.RatingAsync(_factory, slug));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task An_invalid_star_rating_is_rejected_and_nothing_is_saved(int stars)
    {
        const string slug = "mini-metro";
        var c = await Web.MemberAsync(_factory);

        var r = await Web.SaveReviewAsync(c, slug, stars, "x");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("1 to 5", await r.Content.ReadAsStringAsync());
        Assert.Equal(0, (await Web.RatingAsync(_factory, slug)).Count);
    }

    [Fact]
    public async Task An_over_long_review_is_rejected()
    {
        const string slug = "outer-wilds";
        var c = await Web.MemberAsync(_factory);

        var r = await Web.SaveReviewAsync(c, slug, 4, new string('a', 4001));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, (await Web.RatingAsync(_factory, slug)).Count);
    }

    [Fact]
    public async Task Review_text_is_encoded_never_run_as_html()
    {
        const string slug = "factorio";
        var c = await Web.MemberAsync(_factory);

        await Web.SaveReviewAsync(c, slug, 5, "<script>alert('x')</script>");

        var page = await c.GetStringAsync($"/games/{slug}");
        Assert.DoesNotContain("<script>alert", page);
        Assert.Contains("&lt;script&gt;", page);
    }

    [Fact]
    public async Task Reviewing_an_unknown_game_is_a_404()
    {
        var c = await Web.MemberAsync(_factory);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/games/does-not-exist/reviews")).StatusCode);
    }

    [Fact]
    public async Task A_reported_review_can_be_hidden_then_restored_and_the_rating_follows()
    {
        const string slug = "slay-the-spire";
        var author = await Web.MemberAsync(_factory, "Author");
        var reporter = await Web.MemberAsync(_factory, "Reporter");
        var moderator = await Web.InRoleAsync(_factory, Roles.Moderator);
        await Web.SaveReviewAsync(author, slug, 1, "Spoiler-filled rant");
        var reviewId = await ReviewIdAsync(slug);

        // Report: the moderation queue shows it with the reason.
        var report = await Web.PostAsync(reporter, $"/reviews/{reviewId}/report", new() { ["slug"] = slug, ["reason"] = "contains spoilers" }, $"/games/{slug}");
        Assert.Equal(HttpStatusCode.Redirect, report.StatusCode);
        var queue = await moderator.GetStringAsync("/moderation");
        Assert.Contains("Spoiler-filled rant", queue);
        Assert.Contains("contains spoilers", queue);

        // Hide: gone from the page and the rating, reports resolved, but the author still sees a notice.
        await Web.PostAsync(moderator, $"/moderation/{reviewId}/hide", new(), "/moderation");
        Assert.Equal((0, 0.0), await Web.RatingAsync(_factory, slug));
        Assert.DoesNotContain("Spoiler-filled rant", await reporter.GetStringAsync($"/games/{slug}"));
        Assert.Contains("hidden your review", await author.GetStringAsync($"/games/{slug}"));
        var afterHide = await moderator.GetStringAsync("/moderation");
        Assert.Contains("Hidden", afterHide);
        Assert.DoesNotContain("contains spoilers", afterHide);

        // Restore: back on the page and in the rating.
        await Web.PostAsync(moderator, $"/moderation/{reviewId}/restore", new(), "/moderation");
        Assert.Equal((1, 1.0), await Web.RatingAsync(_factory, slug));
        Assert.Contains("Spoiler-filled rant", await reporter.GetStringAsync($"/games/{slug}"));
    }

    [Fact]
    public async Task Dismissing_reports_keeps_the_review_visible()
    {
        const string slug = "terraria";
        var author = await Web.MemberAsync(_factory);
        var reporter = await Web.MemberAsync(_factory);
        var moderator = await Web.InRoleAsync(_factory, Roles.Admin);
        await Web.SaveReviewAsync(author, slug, 4, "Totally fine review");
        var id = await ReviewIdAsync(slug);
        await Web.PostAsync(reporter, $"/reviews/{id}/report", new() { ["slug"] = slug, ["reason"] = "mistaken report" }, $"/games/{slug}");

        await Web.PostAsync(moderator, $"/moderation/{id}/dismiss", new(), "/moderation");

        Assert.DoesNotContain("Totally fine review", await moderator.GetStringAsync("/moderation"));
        Assert.Equal((1, 4.0), await Web.RatingAsync(_factory, slug));
    }

    [Fact]
    public async Task A_member_can_report_a_review_only_once_and_not_their_own()
    {
        const string slug = "valheim";
        var author = await Web.MemberAsync(_factory);
        var other = await Web.MemberAsync(_factory);
        var moderator = await Web.InRoleAsync(_factory, Roles.Moderator);
        await Web.SaveReviewAsync(author, slug, 3, "Valheim-review-under-test");
        var id = await ReviewIdAsync(slug);

        await Web.PostAsync(author, $"/reviews/{id}/report", new() { ["slug"] = slug, ["reason"] = "reason-by-author" }, $"/games/{slug}");
        await Web.PostAsync(other, $"/reviews/{id}/report", new() { ["slug"] = slug, ["reason"] = "reason-one" }, $"/games/{slug}");
        await Web.PostAsync(other, $"/reviews/{id}/report", new() { ["slug"] = slug, ["reason"] = "reason-two" }, $"/games/{slug}");

        // Look only at this review's card: the queue page is shared with other tests' reviews.
        var queue = await moderator.GetStringAsync("/moderation");
        var card = queue.Split("<article").Single(a => a.Contains("Valheim-review-under-test"));
        Assert.Contains("1 report<", card);
        Assert.Contains("reason-one", card);
        Assert.DoesNotContain("reason-two", card);
        Assert.DoesNotContain("reason-by-author", card);
    }

    [Fact]
    public async Task Wishlist_and_played_lists_are_independent_and_personal()
    {
        const string slug = "celeste";
        var me = await Web.MemberAsync(_factory);
        var someoneElse = await Web.MemberAsync(_factory);

        await Web.PostAsync(me, $"/games/{slug}/lists/Wishlist", new() { ["present"] = "true" }, $"/games/{slug}");
        await Web.PostAsync(me, $"/games/{slug}/lists/Wishlist", new() { ["present"] = "true" }, $"/games/{slug}"); // repeat is harmless

        var lists = await me.GetStringAsync("/me/lists");
        Assert.Contains("Celeste", lists);
        Assert.Contains("On your wishlist", await me.GetStringAsync($"/games/{slug}"));
        Assert.DoesNotContain("Celeste", await someoneElse.GetStringAsync("/me/lists"));

        await Web.PostAsync(me, $"/games/{slug}/lists/Played", new() { ["present"] = "true" }, $"/games/{slug}");
        await Web.PostAsync(me, $"/games/{slug}/lists/Wishlist", new() { ["present"] = "false" }, $"/games/{slug}");
        var page = await me.GetStringAsync($"/games/{slug}");
        Assert.Contains("Add to wishlist", page);
        Assert.Contains("&#x2713; Played", page); // Razor HTML-encodes the check mark
    }

    [Fact]
    public async Task An_unknown_list_kind_or_game_is_a_404()
    {
        var me = await Web.MemberAsync(_factory);
        var bad = await Web.PostAsync(me, "/games/celeste/lists/Favourites", new() { ["present"] = "true" }, "/account/profile");
        var missing = await Web.PostAsync(me, "/games/nope/lists/Wishlist", new() { ["present"] = "true" }, "/account/profile");

        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<int> ReviewIdAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVault.Infrastructure.Persistence.GameVaultDbContext>();
        return db.Reviews.Where(r => r.Game!.Slug == slug).OrderByDescending(r => r.Id).Select(r => r.Id).First();
    }
}
