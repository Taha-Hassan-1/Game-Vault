using GameVault.Application.Catalogue;
using GameVault.Application.Reviews;
using GameVault.Web.Models;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

[Authorize]
public class ReviewsController : Controller
{
    private readonly ReviewService _reviews;
    private readonly IGameCatalog _catalog;

    public ReviewsController(ReviewService reviews, IGameCatalog catalog)
    {
        _reviews = reviews;
        _catalog = catalog;
    }

    [HttpGet("games/{slug}/reviews")]
    public async Task<IActionResult> Edit(string slug, CancellationToken ct)
    {
        var game = await _catalog.GetDetailsAsync(slug, ct);
        if (game is null) return NotFound();

        var mine = await _reviews.GetMineAsync(User.UserId()!, slug, ct);
        return View(new ReviewFormModel
        {
            Slug = game.Slug, GameTitle = game.Title, Exists = mine is not null, IsHidden = mine?.IsHidden ?? false,
            Stars = mine?.Stars ?? 0, Body = mine?.Body
        });
    }

    [HttpPost("games/{slug}/reviews")]
    public async Task<IActionResult> Save(string slug, ReviewFormModel model, CancellationToken ct)
    {
        var game = await _catalog.GetDetailsAsync(slug, ct);
        if (game is null) return NotFound();
        model.Slug = game.Slug;
        model.GameTitle = game.Title;
        if (!ModelState.IsValid) return View(nameof(Edit), model);

        var result = await _reviews.SaveAsync(User.UserId()!, slug, model.Stars, model.Body, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(nameof(Edit), model);
        }
        TempData["Message"] = "Thanks, your review is saved.";
        return RedirectToRoute("game", new { slug });
    }

    [HttpPost("games/{slug}/reviews/delete")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        var result = await _reviews.DeleteMineAsync(User.UserId()!, slug, ct);
        TempData["Message"] = result.Ok ? "Your review was deleted." : result.Error;
        return RedirectToRoute("game", new { slug });
    }

    [HttpPost("reviews/{id:int}/report")]
    public async Task<IActionResult> Report(int id, string slug, string? reason, CancellationToken ct)
    {
        var result = await _reviews.ReportAsync(User.UserId()!, id, reason, ct);
        TempData["Message"] = result.Ok ? "Thanks, a moderator will take a look." : result.Error;
        return RedirectToRoute("game", new { slug });
    }
}
