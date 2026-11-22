namespace GameVault.Web.Security;

/// <summary>
/// Response headers that tell the browser what a page may load and do. The policy is strict on purpose:
/// no inline scripts or styles anywhere in the app, so nothing needs 'unsafe-inline' and an injected script cannot run.
/// </summary>
public static class SecurityHeaders
{
    // img-src allows https: because game covers and screenshots come from RAWG's CDN; everything else is same-origin only.
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public const string ImmutableCache = "public,max-age=31536000,immutable";

    /// <summary>
    /// The layout asks for assets as /css/site.css?v=HASH. The hash changes whenever the file does, so a versioned URL can be
    /// cached for a year and never revalidated; a plain URL keeps the framework's default (revalidate with the ETag).
    /// Without this every page view sent four conditional requests for files that had not changed.
    /// </summary>
    public static IApplicationBuilder UseLongCacheForVersionedAssets(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            var versioned = ctx.Request.Query.ContainsKey("v")
                && (ctx.Request.Path.StartsWithSegments("/css") || ctx.Request.Path.StartsWithSegments("/js") || ctx.Request.Path.StartsWithSegments("/lib"));
            if (versioned)
            {
                // After the static-asset handler has set its own header, so this one wins.
                ctx.Response.OnStarting(() =>
                {
                    if (ctx.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                        ctx.Response.Headers.CacheControl = ImmutableCache;
                    return Task.CompletedTask;
                });
            }
            await next();
        });

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            // OnStarting so the headers are present on every response, including errors and static files.
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h.ContentSecurityPolicy = ContentSecurityPolicy;
                h.XContentTypeOptions = "nosniff";
                h.XFrameOptions = "DENY"; // frame-ancestors covers modern browsers; this covers old ones
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            await next();
        });
}
