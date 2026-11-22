using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace GameVault.Smoke;

/// <summary>
/// The four flows the spec names: register, review a game, add to the wishlist, post a suggestion. They run in a real browser
/// against the real site, and every page load is also checked for Content Security Policy violations in the browser console.
/// </summary>
[Collection("Smoke")]
public class SmokeFlows
{
    private readonly SmokeHost _host;
    public SmokeFlows(SmokeHost host) => _host = host;

    /// <summary>Saves a full-page picture when GAMEVAULT_SCREENSHOTS names a folder; used to make the README screenshots.</summary>
    private static async Task Shot(IPage page, string name)
    {
        var dir = Environment.GetEnvironmentVariable("GAMEVAULT_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, name + ".png"), FullPage = true });
    }

    private async Task<(IPage Page, List<string> Violations)> NewPageAsync()
    {
        var context = await _host.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _host.BaseUrl, ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        var problems = new List<string>();
        page.Console += (_, m) =>
        {
            // The browser reports a blocked script, style or handler as a console error that names the policy.
            if (m.Type == "error" && m.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)) problems.Add(m.Text);
        };
        page.PageError += (_, e) => problems.Add("page error: " + e);
        return (page, problems);
    }

    [SmokeFact]
    public async Task A_visitor_can_register_review_a_game_wishlist_it_and_post_a_suggestion()
    {
        var (page, violations) = await NewPageAsync();
        var email = $"smoke-{Guid.NewGuid():N}@example.test";

        // 1. Register through the form.
        await page.GotoAsync("/account/register");
        await page.GetByLabel("Display name").FillAsync("Smoke Tester");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync("Str0ng-Passw0rd!");
        await page.GetByLabel("Confirm password").FillAsync("Str0ng-Passw0rd!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Smoke Tester" })).ToBeVisibleAsync();

        // 2. Review a game: the rating on the game page follows.
        await page.GotoAsync("/games/celeste");
        await page.GetByRole(AriaRole.Link, new() { Name = "Write a review" }).ClickAsync();
        await page.GetByLabel("Rating").SelectOptionAsync("5");
        await page.GetByLabel("Review (optional)").FillAsync("Tight controls and a lovely story.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save review" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Tight controls and a lovely story.")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".gv-stars").First).ToContainTextAsync("5.0");

        // 3. Wishlist: the button changes and the game shows up under My lists.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to wishlist" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "On your wishlist" })).ToBeVisibleAsync();
        await Shot(page, "game-page");
        await page.GotoAsync("/me/lists");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Wishlist" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Celeste" })).ToBeVisibleAsync();
        await Shot(page, "my-lists");

        // 4. Suggestion board on the studio game: post, then upvote.
        await page.GotoAsync("/games/untangle/suggestions");
        await page.GetByLabel("Title").FillAsync("Add a colour-blind palette");
        await page.GetByLabel("Details (optional)").FillAsync("Red and green edges look the same to me.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Post suggestion" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add a colour-blind palette" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Upvote" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Remove your vote" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("article", new() { HasText = "Add a colour-blind palette" })).ToContainTextAsync(new Regex(@"1\s+vote"));
        await Shot(page, "suggestion-board");

        Assert.Empty(violations); // the policy blocked nothing in any of the four flows
    }

    [SmokeFact]
    public async Task The_main_pages_load_without_policy_violations_and_the_navigation_works()
    {
        var (page, violations) = await NewPageAsync();

        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Find your next game");
        await Shot(page, "home");

        // The collapsed menu on a narrow screen is Bootstrap JavaScript: it must work under the policy.
        await page.SetViewportSizeAsync(390, 800);
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" }).ClickAsync();
        var browse = page.GetByRole(AriaRole.Link, new() { Name = "Browse", Exact = true }); // not the hero's "Browse games" button
        await Assertions.Expect(browse).ToBeVisibleAsync();
        await browse.ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Browse games" })).ToBeVisibleAsync();

        await page.SetViewportSizeAsync(1280, 900);
        await page.GetByLabel("Genre").SelectOptionAsync(new SelectOptionValue { Label = "Puzzle" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Monument Valley")).ToBeVisibleAsync();
        await Shot(page, "catalogue");

        await page.GotoAsync("/games/hades");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Hades" }).First).ToBeVisibleAsync();
        await page.GotoAsync("/does-not-exist");
        await Assertions.Expect(page.GetByText("We could not find that page")).ToBeVisibleAsync();

        Assert.Empty(violations);
    }
}
