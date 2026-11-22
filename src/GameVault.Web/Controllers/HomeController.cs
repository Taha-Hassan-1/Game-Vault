using System.Diagnostics;
using GameVault.Application.Catalogue;
using GameVault.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class HomeController : Controller
{
    private readonly IGameCatalog _catalog;
    public HomeController(IGameCatalog catalog) => _catalog = catalog;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _catalog.GetHomeAsync(ct));

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
