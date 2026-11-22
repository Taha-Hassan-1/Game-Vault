using System.Security.Claims;
using GameVault.Domain;
using GameVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Infrastructure.Identity;

/// <summary>Puts the display name in the cookie so every page can greet the member without a database call.</summary>
public sealed class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole>
{
    public const string DisplayNameClaim = "display_name";

    public AppUserClaimsPrincipalFactory(UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
        : base(users, roles, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        return identity;
    }
}

public static class IdentitySeeder
{
    /// <summary>
    /// Creates the three roles and, when Admin:Email and Admin:Password are configured (environment variables in production),
    /// the first Admin. The password is never stored in the repo.
    /// </summary>
    public static async Task EnsureAsync(IServiceProvider services, IConfiguration config)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Member, Roles.Moderator, Roles.Admin })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Studio" };
        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder")
                .LogError("Could not create the first admin: {Errors}", string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRolesAsync(admin, new[] { Roles.Admin, Roles.Moderator, Roles.Member });
    }
}
