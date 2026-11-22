using GameVault.Application.Catalogue;
using GameVault.Application.Metadata;
using GameVault.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class GamesController : Controller
{
    private readonly IGameCatalog _catalog;
    private readonly GameImportService _import;

    public GamesController(IGameCatalog catalog, GameImportService import)
    {
        _catalog = catalog;
        _import = import;
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
        return game is null ? NotFound() : View(game);
    }
}
