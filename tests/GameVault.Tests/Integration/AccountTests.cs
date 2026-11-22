using System.Net;

namespace GameVault.Tests.Integration;

public class AccountTests : IClassFixture<GameVaultFactory>
{
    private readonly GameVaultFactory _factory;
    public AccountTests(GameVaultFactory factory) => _factory = factory;

    [Fact]
    public async Task Registering_signs_the_member_in_and_shows_their_name()
    {
        var c = Web.NewClient(_factory);

        var r = await Web.RegisterAsync(c, Web.NewEmail(), "Ada Lovelace");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var home = await c.GetStringAsync("/");
        Assert.Contains("Ada Lovelace", home);
        Assert.Contains("Sign out", home);
    }

    [Fact]
    public async Task A_duplicate_email_is_refused()
    {
        var email = Web.NewEmail();
        await Web.RegisterAsync(Web.NewClient(_factory), email);

        var second = await Web.RegisterAsync(Web.NewClient(_factory), email);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // form shown again
        Assert.Contains("already taken", await second.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Short1!", "at least 10")]
    [InlineData("alllowercase1234", "uppercase")]
    [InlineData("NoDigitsHereAtAll", "digit")]
    public async Task Weak_passwords_are_refused(string password, string expected)
    {
        var r = await Web.RegisterAsync(Web.NewClient(_factory), Web.NewEmail(), "Weak", password);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(expected, await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_in_with_a_wrong_password_gives_one_generic_message()
    {
        var email = Web.NewEmail();
        await Web.RegisterAsync(Web.NewClient(_factory), email);
        var c = Web.NewClient(_factory);

        var wrongPassword = await Web.LoginAsync(c, email, "Wrong-Passw0rd!");
        var unknownUser = await Web.LoginAsync(c, "nobody@example.test");

        foreach (var r in new[] { wrongPassword, unknownUser })
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("Invalid email or password", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_for_the_right_password()
    {
        var email = Web.NewEmail();
        await Web.RegisterAsync(Web.NewClient(_factory), email);
        var c = Web.NewClient(_factory);

        for (var i = 0; i < 5; i++) await Web.LoginAsync(c, email, "Wrong-Passw0rd!");
        var r = await Web.LoginAsync(c, email);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Too many failed attempts", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sign_in_follows_a_local_return_url_but_never_an_external_one()
    {
        var email = Web.NewEmail();
        await Web.RegisterAsync(Web.NewClient(_factory), email);

        var local = await Web.LoginAsync(Web.NewClient(_factory), email, returnUrl: "/games/hades");
        var external = await Web.LoginAsync(Web.NewClient(_factory), email, returnUrl: "https://evil.example/phish");

        Assert.Equal("/games/hades", local.Headers.Location!.OriginalString);
        Assert.Equal("/", external.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var c = await Web.MemberAsync(_factory);

        var r = await Web.PostAsync(c, "/account/logout", new(), "/account/profile");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/me/lists")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_display_name_shows_up_straight_away()
    {
        var c = await Web.MemberAsync(_factory, "Old Name");

        var r = await Web.PostAsync(c, "/account/profile", new() { ["DisplayName"] = "New Name" }, "/account/profile");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var home = await c.GetStringAsync("/");
        Assert.Contains("New Name", home);
        Assert.DoesNotContain("Old Name", home);
    }
}
