using GameVault.Application.Catalogue;
using GameVault.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class GamesController : Controller
{
    private readonly IGameCatalog _catalog;
    public GamesController(IGameCatalog catalog) => _catalog = catalog;

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
        var game = await _catalog.GetDetailsAsync(slug, ct);
        return game is null ? NotFound() : View(game);
    }
}
