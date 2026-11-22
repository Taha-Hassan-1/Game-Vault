using GameVault.Domain;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Identity;
using GameVault.Infrastructure.Persistence;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Every POST is checked for an anti-forgery token unless a controller opts out.
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddGameVault(builder.Configuration);

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        o.SignIn.RequireConfirmedEmail = false; // switch on once a mail sender exists
    })
    .AddEntityFrameworkStores<GameVaultDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/denied";
    o.Cookie.Name = ".GameVault.Auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});

// Controllers state intent through named policies instead of role strings.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanModerate, p => p.RequireRole(Roles.Moderator, Roles.Admin))
    .AddPolicy(Policies.StudioOwner, p => p.RequireRole(Roles.Admin));
builder.Services.AddResponseCompression();
builder.Services.AddHealthChecks().AddDbContextCheck<GameVaultDbContext>("database");

var app = builder.Build();

// Production applies migrations as an explicit step; this flag exists for first-run convenience only.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStart"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
    await IdentitySeeder.EnsureAsync(scope.ServiceProvider, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapHealthChecks("/health");
app.MapControllerRoute("game", "games/{slug}", new { controller = "Games", action = "Details" });
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();

app.Run();

public partial class Program { }
