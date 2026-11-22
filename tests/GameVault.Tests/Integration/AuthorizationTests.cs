using System.Net;
using GameVault.Domain;

namespace GameVault.Tests.Integration;

/// <summary>
/// Cookie authentication answers an anonymous request with a redirect to the sign-in page (the browser equivalent of 401)
/// and a signed-in member without the role with a redirect to the access-denied page (the equivalent of 403).
/// </summary>
public class AuthorizationTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public AuthorizationTests(GameVaultFactory factory) => _factory = factory;

    public static TheoryData<string> MemberRoutes => new() { "/me/lists", "/games/hades/reviews", "/account/profile" };

    [Theory]
    [MemberData(nameof(MemberRoutes))]
    [InlineData("/moderation")]
    public async Task Anonymous_visitors_are_sent_to_sign_in(string url)
    {
        var r = await Web.NewClient(_factory).GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/login", r.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/games")]
    [InlineData("/games/hades")]
    [InlineData("/account/login")]
    [InlineData("/account/register")]
    public async Task Public_pages_stay_open_to_anonymous_visitors(string url)
        => Assert.Equal(HttpStatusCode.OK, (await Web.NewClient(_factory).GetAsync(url)).StatusCode);

    [Theory]
    [MemberData(nameof(MemberRoutes))]
    public async Task Members_can_open_member_pages(string url)
    {
        var member = await Web.MemberAsync(_factory);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_member_is_denied_the_moderation_page()
    {
        var member = await Web.MemberAsync(_factory);

        var r = await member.GetAsync("/moderation");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/denied", r.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(Roles.Moderator)]
    [InlineData(Roles.Admin)]
    public async Task Moderators_and_admins_can_open_the_moderation_page(string role)
    {
        var client = await Web.InRoleAsync(_factory, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/moderation")).StatusCode);
    }

    [Fact]
    public async Task A_member_cannot_post_moderation_actions()
    {
        var member = await Web.MemberAsync(_factory);

        var r = await Web.PostAsync(member, "/moderation/1/hide", new(), "/account/profile");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/denied", r.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/account/logout")]
    [InlineData("/games/hades/reviews")]
    [InlineData("/games/hades/lists/Wishlist")]
    public async Task Posts_without_an_anti_forgery_token_are_rejected(string url)
    {
        var member = await Web.MemberAsync(_factory);
        var r = await member.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task The_navigation_shows_moderation_only_to_moderators()
    {
        var member = await Web.MemberAsync(_factory);
        var moderator = await Web.InRoleAsync(_factory, Roles.Moderator);

        Assert.DoesNotContain("/moderation", await member.GetStringAsync("/"));
        Assert.Contains("/moderation", await moderator.GetStringAsync("/"));
    }
}
