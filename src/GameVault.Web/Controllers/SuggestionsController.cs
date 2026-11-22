using GameVault.Application.Catalogue;
using GameVault.Application.Feedback;
using GameVault.Application.Suggestions;
using GameVault.Domain;
using GameVault.Web.Models;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class SuggestionsController : Controller
{
    private readonly SuggestionService _suggestions;
    public SuggestionsController(SuggestionService suggestions) => _suggestions = suggestions;

    [HttpGet("games/{slug}/suggestions")]
    public async Task<IActionResult> Index(string slug, string? status, CancellationToken ct)
    {
        var game = await _suggestions.GetGameAsync(slug, ct);
        if (game is null || !game.IsStudioGame) return NotFound();
        return View(await Board(game, status, new SuggestionForm(), ct));
    }

    [Authorize, HttpPost("games/{slug}/suggestions")]
    public async Task<IActionResult> Submit(string slug, SuggestionForm form, CancellationToken ct)
    {
        var game = await _suggestions.GetGameAsync(slug, ct);
        if (game is null || !game.IsStudioGame) return NotFound();

        if (ModelState.IsValid)
        {
            var result = await _suggestions.SubmitAsync(User.UserId()!, slug, form.Title, form.Body, ct);
            if (result.Ok)
            {
                TempData["Message"] = "Thanks, your suggestion is on the board.";
                return RedirectToAction(nameof(Index), new { slug });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return View(nameof(Index), await Board(game, null, form, ct));
    }

    [Authorize, HttpPost("suggestions/{id:int}/vote")]
    public async Task<IActionResult> Vote(int id, string slug, bool present, CancellationToken ct)
    {
        var result = await _suggestions.VoteAsync(User.UserId()!, id, present, ct);
        if (!result.Ok) return NotFound();
        return RedirectToAction(nameof(Index), new { slug });
    }

    [Authorize(Policy = Policies.StudioOwner), HttpPost("suggestions/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, string slug, string status, CancellationToken ct)
    {
        if (!Enum.TryParse<SuggestionStatus>(status, ignoreCase: true, out var s)) return NotFound();
        var result = await _suggestions.SetStatusAsync(id, s, ct);
        if (!result.Ok) return NotFound();
        return RedirectToAction(nameof(Index), new { slug });
    }

    private async Task<SuggestionBoardViewModel> Board(SuggestionGame game, string? status, SuggestionForm form, CancellationToken ct)
    {
        SuggestionStatus? filter = Enum.TryParse<SuggestionStatus>(status, true, out var s) && Enum.IsDefined(s) ? s : null;
        var signedIn = User.Identity?.IsAuthenticated == true;
        var userId = signedIn ? User.UserId() : null;
        var items = await _suggestions.ListAsync(game.Id, userId, filter, ct);
        return new SuggestionBoardViewModel(game, items, filter, signedIn, User.IsInRole(Roles.Admin), form);
    }
}

[Authorize]
public class FeedbackController : Controller
{
    private readonly FeedbackService _feedback;
    private readonly IGameCatalog _catalog;

    public FeedbackController(FeedbackService feedback, IGameCatalog catalog)
    {
        _feedback = feedback;
        _catalog = catalog;
    }

    [HttpGet("games/{slug}/feedback")]
    public async Task<IActionResult> Send(string slug, CancellationToken ct)
    {
        var game = await _catalog.GetDetailsAsync(slug, ct);
        if (game is null || !game.IsStudioGame) return NotFound();
        return View(new FeedbackForm { Slug = game.Slug, GameTitle = game.Title });
    }

    [HttpPost("games/{slug}/feedback")]
    public async Task<IActionResult> Send(string slug, FeedbackForm form, CancellationToken ct)
    {
        var game = await _catalog.GetDetailsAsync(slug, ct);
        if (game is null || !game.IsStudioGame) return NotFound();
        form.Slug = game.Slug;
        form.GameTitle = game.Title;

        if (ModelState.IsValid)
        {
            var name = User.FindFirst("display_name")?.Value;
            var result = await _feedback.SubmitAsync(
                new FeedbackSubmission(game.Id, FeedbackSource.Web, form.Category, form.Message, name, null), ct);
            if (result.Ok)
            {
                TempData["Message"] = "Thanks, your feedback reached the studio.";
                return RedirectToRoute("game", new { slug });
            }
            foreach (var (field, messages) in result.Errors)
                foreach (var m in messages) ModelState.AddModelError(string.Empty, m);
        }
        return View(form);
    }
}
