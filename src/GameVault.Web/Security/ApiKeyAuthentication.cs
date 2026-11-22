using System.Security.Claims;
using System.Text.Encodings.Web;
using GameVault.Application.Studio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Security;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
    public const string GameClaim = "game_id";
}

/// <summary>Authenticates a Unity build by its X-Api-Key header and ties the request to exactly one game.</summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiKeyService _keys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ApiKeyService keys)
        : base(options, logger, encoder) => _keys = keys;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.Header, out var header) || header.Count != 1)
            return AuthenticateResult.NoResult();

        var gameId = await _keys.AuthenticateAsync(header[0], Context.RequestAborted);
        if (gameId is null) return AuthenticateResult.Fail("Invalid or revoked API key.");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "api-key"),
            new Claim(ApiKeyDefaults.GameClaim, gameId.Value.ToString())
        }, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = ApiKeyDefaults.Scheme;
        await ApiProblems.WriteAsync(Context, StatusCodes.Status401Unauthorized, "A valid API key is required.",
            "Send an active key in the X-Api-Key header.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => ApiProblems.WriteAsync(Context, StatusCodes.Status403Forbidden, "This key cannot do that.", null);
}

public static class ApiProblems
{
    public static Task WriteAsync(HttpContext ctx, int status, string title, string? detail)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            options: null, contentType: "application/problem+json", cancellationToken: ctx.RequestAborted);
    }
}
