using System.Net;
using System.Text.RegularExpressions;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

/// <summary>Drives the real site over HTTP the way a browser does: cookies, anti-forgery tokens and form posts.</summary>
internal static class Web
{
    public const string Password = "Str0ng-Passw0rd!";
    private static int _counter;

    public static string NewEmail() => $"user{Interlocked.Increment(ref _counter)}@example.test";

    public static HttpClient NewClient(GameVaultFactory f)
        => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Anti-forgery tokens are bound to the current user, so fetch one from a page while in the state that will post it.</summary>
    public static async Task<string> TokenAsync(HttpClient c, string pageUrl)
    {
        var html = await c.GetStringAsync(pageUrl);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, $"no anti-forgery token on {pageUrl}");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient c, string url, Dictionary<string, string> form, string tokenPage)
    {
        form["__RequestVerificationToken"] = await TokenAsync(c, tokenPage);
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    public static async Task<HttpResponseMessage> RegisterAsync(HttpClient c, string email, string name = "Tester", string password = Password)
        => await PostAsync(c, "/account/register",
            new() { ["Email"] = email, ["DisplayName"] = name, ["Password"] = password, ["ConfirmPassword"] = password }, "/account/register");

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient c, string email, string password = Password, string? returnUrl = null)
        => await PostAsync(c, "/account/login" + (returnUrl is null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl)),
            new() { ["Email"] = email, ["Password"] = password }, "/account/login");

    /// <summary>A signed-in member client, registered through the real form.</summary>
    public static async Task<HttpClient> MemberAsync(GameVaultFactory f, string name = "Tester")
    {
        var c = NewClient(f);
        var r = await RegisterAsync(c, NewEmail(), name);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    /// <summary>Creates a user with the given role directly, then signs in so the role is in the cookie.</summary>
    public static async Task<HttpClient> InRoleAsync(GameVaultFactory f, string role)
    {
        var email = NewEmail();
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, DisplayName = role + " user", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            await users.AddToRoleAsync(user, role);
        }
        var c = NewClient(f);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(c, email)).StatusCode);
        return c;
    }

    public static async Task<HttpResponseMessage> SaveReviewAsync(HttpClient c, string slug, int stars, string body)
        => await PostAsync(c, $"/games/{slug}/reviews", new() { ["Stars"] = stars.ToString(), ["Body"] = body }, $"/games/{slug}/reviews");

    public static async Task<(int Count, double Average)> RatingAsync(GameVaultFactory f, string slug)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var g = db.Games.Single(x => x.Slug == slug);
        return (g.RatingCount, g.RatingAverage);
    }
}
