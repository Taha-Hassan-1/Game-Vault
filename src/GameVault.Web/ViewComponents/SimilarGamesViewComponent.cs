using GameVault.Application.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.ViewComponents;

public class SimilarGamesViewComponent : ViewComponent
{
    private readonly IGameCatalog _catalog;
    public SimilarGamesViewComponent(IGameCatalog catalog) => _catalog = catalog;

    public async Task<IViewComponentResult> InvokeAsync(string slug, int count = 6)
        => View(await _catalog.GetSimilarAsync(slug, count, HttpContext.RequestAborted));
}
