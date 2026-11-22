using System.Net;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public class SuggestionTests : IClassFixture<GameVaultFactory>
{
    private const string Board = "/games/untangle/suggestions";
    private readonly GameVaultFactory _factory;
    public SuggestionTests(GameVaultFactory factory) => _factory = factory;

    private static Task<HttpResponseMessage> Submit(HttpClient c, string title, string? body = null)
        => Web.PostAsync(c, Board, new() { ["Title"] = title, ["Body"] = body ?? "" }, Board);

    private async Task<int> SuggestionIdAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GameVaultDbContext>().Suggestions.Single(s => s.Title == title).Id;
    }

    private async Task<(int Votes, int Rows)> VotesAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        return (db.Suggestions.Single(s => s.Id == id).VoteCount, db.SuggestionVotes.Count(v => v.SuggestionId == id));
    }

    private static Task<HttpResponseMessage> Vote(HttpClient c, int id, bool present)
        => Web.PostAsync(c, $"/suggestions/{id}/vote", new() { ["slug"] = "untangle", ["present"] = present.ToString().ToLowerInvariant() }, Board);

    [Fact]
    public async Task The_board_is_public_but_only_for_studio_games()
    {
        Assert.Equal(HttpStatusCode.OK, (await Web.NewClient(_factory).GetAsync(Board)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Web.NewClient(_factory).GetAsync("/games/hades/suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Web.NewClient(_factory).GetAsync("/games/nope/suggestions")).StatusCode);
    }

    [Fact]
    public async Task Visitors_must_sign_in_to_post_or_vote()
    {
        var anon = Web.NewClient(_factory);
        var viaMember = await Web.MemberAsync(_factory);
        await Submit(viaMember, "Anonymous cannot vote on this");
        var id = await SuggestionIdAsync("Anonymous cannot vote on this");

        // An anonymous client has no session to bind a token to, so borrow a page's token from the signed-out login form.
        var post = await Web.PostAsync(anon, Board, new() { ["Title"] = "Sneaky anonymous post" }, "/account/login");
        var vote = await Web.PostAsync(anon, $"/suggestions/{id}/vote", new() { ["slug"] = "untangle", ["present"] = "true" }, "/account/login");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("/account/login", post.Headers.Location!.ToString());
        Assert.Contains("/account/login", vote.Headers.Location!.ToString());
        Assert.Equal((0, 0), await VotesAsync(id));
    }

    [Fact]
    public async Task A_member_can_post_a_suggestion_and_everyone_sees_it()
    {
        var member = await Web.MemberAsync(_factory, "Idea Person");

        var r = await Submit(member, "Add a colour-blind palette", "Red and green edges look the same to me.");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var page = await Web.NewClient(_factory).GetStringAsync(Board);
        Assert.Contains("Add a colour-blind palette", page);
        Assert.Contains("Red and green edges", page);
        Assert.Contains("Idea Person", page);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("   ")]
    public async Task A_title_that_is_too_short_is_refused(string title)
    {
        var member = await Web.MemberAsync(_factory);
        var r = await Submit(member, title);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // form shown again with the error
    }

    [Fact]
    public async Task Suggestions_cannot_be_posted_to_a_game_that_is_not_the_studios()
    {
        var member = await Web.MemberAsync(_factory);
        var r = await Web.PostAsync(member, "/games/hades/suggestions", new() { ["Title"] = "Make Hades easier" }, Board);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Text_is_encoded_never_run_as_html()
    {
        var member = await Web.MemberAsync(_factory);
        await Submit(member, "<img src=x onerror=alert(1)> title", "<script>alert(2)</script>");

        var page = await member.GetStringAsync(Board);
        Assert.DoesNotContain("<script>alert(2)", page);
        Assert.DoesNotContain("<img src=x", page);
        Assert.Contains("&lt;script&gt;", page);
    }

    [Fact]
    public async Task One_vote_per_member_the_count_follows_the_vote_rows_and_votes_can_be_withdrawn()
    {
        var author = await Web.MemberAsync(_factory);
        var a = await Web.MemberAsync(_factory);
        var b = await Web.MemberAsync(_factory);
        await Submit(author, "Voting test suggestion");
        var id = await SuggestionIdAsync("Voting test suggestion");

        await Vote(a, id, true);
        await Vote(a, id, true); // a second click changes nothing
        Assert.Equal((1, 1), await VotesAsync(id));

        await Vote(b, id, true);
        Assert.Equal((2, 2), await VotesAsync(id));

        await Vote(a, id, false);
        await Vote(a, id, false);
        Assert.Equal((1, 1), await VotesAsync(id));
    }

    [Fact]
    public async Task Simultaneous_votes_from_many_members_are_all_counted_exactly_once()
    {
        var author = await Web.MemberAsync(_factory);
        await Submit(author, "Concurrent voting suggestion");
        var id = await SuggestionIdAsync("Concurrent voting suggestion");
        var voters = new List<HttpClient>();
        for (var i = 0; i < 6; i++) voters.Add(await Web.MemberAsync(_factory));

        // Each voter double-clicks, all at once.
        await Task.WhenAll(voters.SelectMany(v => new[] { Vote(v, id, true), Vote(v, id, true) }));

        Assert.Equal((6, 6), await VotesAsync(id));
    }

    [Fact]
    public async Task The_page_shows_which_suggestions_you_voted_for_and_ranks_by_votes()
    {
        var author = await Web.MemberAsync(_factory);
        var voter = await Web.MemberAsync(_factory);
        await Submit(author, "Rank-test low");
        await Submit(author, "Rank-test high");
        await Vote(voter, await SuggestionIdAsync("Rank-test high"), true);

        var page = await voter.GetStringAsync(Board);

        Assert.True(page.IndexOf("Rank-test high", StringComparison.Ordinal) < page.IndexOf("Rank-test low", StringComparison.Ordinal));
        Assert.Contains("Remove your vote", page);
    }

    [Fact]
    public async Task Voting_on_a_missing_suggestion_is_a_404()
    {
        var member = await Web.MemberAsync(_factory);
        Assert.Equal(HttpStatusCode.NotFound, (await Vote(member, 999999, true)).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_set_a_status_and_the_board_can_be_filtered_by_it()
    {
        var member = await Web.MemberAsync(_factory);
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        await Submit(member, "Status-test planned");
        await Submit(member, "Status-test untouched");
        var id = await SuggestionIdAsync("Status-test planned");

        var byMember = await Web.PostAsync(member, $"/suggestions/{id}/status", new() { ["slug"] = "untangle", ["status"] = "Shipped" }, Board);
        Assert.Contains("/account/denied", byMember.Headers.Location!.ToString());
        Assert.DoesNotContain("Set status", await member.GetStringAsync(Board));

        var byAdmin = await Web.PostAsync(admin, $"/suggestions/{id}/status", new() { ["slug"] = "untangle", ["status"] = "Planned" }, Board);
        Assert.Equal(HttpStatusCode.Redirect, byAdmin.StatusCode);

        var planned = await member.GetStringAsync(Board + "?status=Planned");
        Assert.Contains("Status-test planned", planned);
        Assert.DoesNotContain("Status-test untouched", planned);
        Assert.Contains("Set status", await admin.GetStringAsync(Board));
    }

    [Fact]
    public async Task An_unknown_status_is_a_404_and_an_unknown_filter_is_ignored()
    {
        var admin = await Web.InRoleAsync(_factory, Roles.Admin);
        var member = await Web.MemberAsync(_factory);
        await Submit(member, "Bad status suggestion");
        var id = await SuggestionIdAsync("Bad status suggestion");

        var r = await Web.PostAsync(admin, $"/suggestions/{id}/status", new() { ["slug"] = "untangle", ["status"] = "Banana" }, Board);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(Board + "?status=Banana")).StatusCode);
    }
}
