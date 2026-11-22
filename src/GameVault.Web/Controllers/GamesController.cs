using GameVault.Application.Catalogue;
using GameVault.Application.Lists;
using GameVault.Application.Metadata;
using GameVault.Application.Reviews;
using GameVault.Domain;
using GameVault.Web.Models;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class GamesController : Controller
{
    private readonly IGameCatalog _catalog;
    private readonly GameImportService _import;
    private readonly ListService _lists;
    private readonly ReviewService _reviews;

    public GamesController(IGameCatalog catalog, GameImportService import, ListService lists, ReviewService reviews)
    {
        _catalog = catalog;
        _import = import;
        _lists = lists;
        _reviews = reviews;
    }

    [HttpGet("games")]
    public async Task<IActionResult> Index([FromQuery] CatalogueQuery query, CancellationToken ct)
    {
        var q = query.Normalized();
        var result = await _catalog.SearchAsync(q, ct);
        var facets = await _catalog.GetFacetsAsync(ct);
        return View(new CatalogueViewModel(q, result, facets));
    }

    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        // Lazy details: fetched on first view and spread over real traffic. Never throws; an outage serves the stored copy.
        await _import.EnsureDetailsAsync(slug, ct);
        var game = await _catalog.GetDetailsAsync(slug, ct);
        if (game is null) return NotFound();

        // The cached page model is shared by everyone; what depends on the signed-in member is loaded beside it.
        var userId = User.Identity?.IsAuthenticated == true ? User.UserId() : null;
        var lists = userId is null ? new HashSet<ListKind>() : await _lists.GetStateAsync(userId, game.Slug, ct);
        var mine = userId is null ? null : await _reviews.GetMineAsync(userId, game.Slug, ct);
        return View(new GameDetailsViewModel(game, userId, lists, mine));
    }
}
