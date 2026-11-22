using System.Net;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Integration;

public sealed class AdminConfiguredFactory : GameVaultFactory
{
    public const string Email = "owner@example.test";
    public const string AdminPassword = "Owner-Passw0rd-123";

    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        ["Admin:Email"] = Email,
        ["Admin:Password"] = AdminPassword
    };
}

public class AdminSeedTests : IClassFixture<AdminConfiguredFactory>
{
    private readonly AdminConfiguredFactory _factory;
    public AdminSeedTests(AdminConfiguredFactory factory) => _factory = factory;

    [Fact]
    public async Task The_first_admin_is_created_from_configuration_with_all_three_roles()
    {
        var c = Web.NewClient(_factory);
        await c.GetAsync("/"); // boots the host, which seeds

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await users.FindByEmailAsync(AdminConfiguredFactory.Email);

        Assert.NotNull(admin);
        var roles = await users.GetRolesAsync(admin!);
        Assert.Contains(Roles.Admin, roles);
        Assert.Contains(Roles.Moderator, roles);
        Assert.Contains(Roles.Member, roles);
        Assert.NotEqual(AdminConfiguredFactory.AdminPassword, admin!.PasswordHash); // only a hash is stored
    }

    [Fact]
    public async Task The_seeded_admin_can_sign_in_and_reach_the_moderation_page()
    {
        var c = Web.NewClient(_factory);

        var login = await Web.LoginAsync(c, AdminConfiguredFactory.Email, AdminConfiguredFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/moderation")).StatusCode);
    }

    [Fact]
    public async Task Without_admin_settings_no_admin_exists()
    {
        await using var plain = new GameVaultFactory();
        await Web.NewClient(plain).GetAsync("/");

        using var scope = plain.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Empty(await users.GetUsersInRoleAsync(Roles.Admin));
    }
}
