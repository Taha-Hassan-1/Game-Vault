using System.Threading.RateLimiting;
using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Identity;
using GameVault.Infrastructure.Persistence;
using GameVault.Infrastructure.Storage;
using GameVault.Web.Controllers.Api;
using GameVault.Web.Health;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Every POST is checked for an anti-forgery token unless a controller opts out.
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddGameVault(builder.Configuration);

// Cookies are Secure outside Development. Turn Security:SecureCookies off only for plain-http trials such as docker compose on localhost.
var secureCookies = builder.Configuration.GetValue("Security:SecureCookies", !builder.Environment.IsDevelopment());
var cookieSecurity = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

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
    o.Cookie.SecurePolicy = cookieSecurity;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = ".GameVault.Antiforgery";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSecurity;
});

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

// Controllers state intent through named policies instead of role strings.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanModerate, p => p.RequireRole(Roles.Moderator, Roles.Admin))
    .AddPolicy(Policies.StudioOwner, p => p.RequireRole(Roles.Admin));

// The Unity feedback endpoint authenticates with an API key instead of the cookie.
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);

// Limits are per API key, plus a looser per-address cap on /api so rotating made-up keys does not buy extra requests.
var keyPermit = builder.Configuration.GetValue("FeedbackApi:PermitPerMinute", 30);
var ipPermit = builder.Configuration.GetValue("FeedbackApi:IpPermitPerMinute", 120);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await ApiProblems.WriteAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests, "Too many requests.", "Slow down and retry shortly.");
    };
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter("ip:" + ctx.Connection.RemoteIpAddress,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ipPermit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("site"));
    o.AddPolicy(RateLimitPolicies.Feedback, ctx =>
    {
        var key = ctx.Request.Headers[ApiKeyDefaults.Header].ToString();
        var partition = key.Length > 0 ? "key:" + KeyHasher.Hash(key) : "anon:" + ctx.Connection.RemoteIpAddress;
        return RateLimitPartition.GetFixedWindowLimiter(partition,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = keyPermit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});

builder.Services.AddResponseCompression();

builder.Services.AddHttpClient(RawgHealthCheck.ClientName);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<GameVaultDbContext>("database", tags: new[] { HealthTags.Core })
    .AddCheck<RawgHealthCheck>("rawg", failureStatus: HealthStatus.Degraded, tags: new[] { HealthTags.External });

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

// Outermost, so the log line records the final status of every request, including failures.
app.UseSerilogRequestLogging();
app.UseSecurityHeaders();
app.UseLongCacheForVersionedAssets();

// Errors: the API answers with a problem document, the site with a friendly page. Neither shows exception details.
// (Development keeps the developer exception page for the site; the API handler runs in every environment.)
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api => api.UseExceptionHandler(handler => handler.Run(ctx =>
    ApiProblems.WriteAsync(ctx, StatusCodes.Status500InternalServerError, "An unexpected error occurred.",
        "It has been logged. Quote the traceId if you contact the studio."))));
if (!app.Environment.IsDevelopment())
{
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), web => web.UseExceptionHandler("/Home/Error"));
    app.UseHsts();
}

// 404s and the like: friendly pages for the site, problem documents for the API.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api => api.UseStatusCodePages(ctx =>
    ApiProblems.WriteAsync(ctx.HttpContext, ctx.HttpContext.Response.StatusCode, StatusTitle(ctx.HttpContext.Response.StatusCode), null)));
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), web => web.UseStatusCodePagesWithReExecute("/Home/Status/{0}"));

// Kestrel rejects an oversized or broken body by throwing; on the API that becomes a plain problem response
// with Kestrel's own status (for example 413) instead of an error page and a logged 500.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (BadHttpRequestException ex) when (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
    {
        await ApiProblems.WriteAsync(ctx, ex.StatusCode,
            ex.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request is too large." : "The request could not be read.", null);
    }
});

app.UseResponseCompression();
app.UseHttpsRedirection();

// Uploaded studio images live outside wwwroot, so they are served from their own folder.
var uploads = (FileImageStore)app.Services.GetRequiredService<IImageStore>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploads.Root),
    RequestPath = FileImageStore.RequestPath,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable"; // names are random and never reused
    }
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

// /health is what a load balancer probes: the database only. /health/rawg reports the external dependency separately,
// so a RAWG outage never takes the site out of rotation.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains(HealthTags.Core),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/rawg", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains(HealthTags.External),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK, // reported in the body; a RAWG outage is not a site outage
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapControllerRoute("game", "games/{slug}", new { controller = "Games", action = "Details" });
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();

app.Run();

static string StatusTitle(int status) => status switch
{
    StatusCodes.Status400BadRequest => "The request is not valid.",
    StatusCodes.Status401Unauthorized => "A valid API key is required.",
    StatusCodes.Status403Forbidden => "This key cannot do that.",
    StatusCodes.Status404NotFound => "There is nothing at this address.",
    StatusCodes.Status405MethodNotAllowed => "This method is not allowed here.",
    StatusCodes.Status415UnsupportedMediaType => "Send the body as application/json.",
    _ => "The request could not be completed."
};

public partial class Program { }
