using GameVault.Application.Feedback;
using GameVault.Domain;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GameVault.Web.Controllers.Api;

public sealed class FeedbackRequest
{
    public string? Category { get; set; }
    public string? Body { get; set; }
    public string? PlayerRef { get; set; }
    public string? AppVersion { get; set; }
}

/// <summary>
/// Versioned endpoint the Unity builds post to. Cookie-free, so it opts out of the site-wide anti-forgery check;
/// the API key is the credential.
/// </summary>
[ApiController]
[Route("api/v1/feedback")]
[IgnoreAntiforgeryToken]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[EnableRateLimiting(RateLimitPolicies.Feedback)]
[RequestSizeLimit(FeedbackApiController.MaxRequestBytes)]
public class FeedbackApiController : ControllerBase
{
    public const int MaxRequestBytes = 16 * 1024;

    private readonly FeedbackService _feedback;
    public FeedbackApiController(FeedbackService feedback) => _feedback = feedback;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Post([FromBody] FeedbackRequest request, CancellationToken ct)
    {
        // Kestrel enforces the attribute above; this also covers hosts that do not.
        if (Request.ContentLength > MaxRequestBytes)
            return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The request is too large.");

        var gameId = int.Parse(User.FindFirst(ApiKeyDefaults.GameClaim)!.Value);
        var result = await _feedback.SubmitAsync(
            new FeedbackSubmission(gameId, FeedbackSource.Api, request.Category, request.Body, request.PlayerRef, request.AppVersion), ct);

        if (!result.Ok) return ValidationProblem(new ValidationProblemDetails(result.Errors.ToDictionary(e => e.Key, e => e.Value)));
        return StatusCode(StatusCodes.Status201Created, new { id = result.Id });
    }
}

public static class RateLimitPolicies
{
    public const string Feedback = "feedback";
}
