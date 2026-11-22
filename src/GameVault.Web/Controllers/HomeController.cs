using System.Diagnostics;
using GameVault.Application.Catalogue;
using GameVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

public class HomeController : Controller
{
    private readonly IGameCatalog _catalog;
    public HomeController(IGameCatalog catalog) => _catalog = catalog;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _catalog.GetHomeAsync(ct));

    /// <summary>Reached by the exception handler. Shows a reference id, never the exception.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, StatusCode = 500 });

    /// <summary>Reached by the status-code pages middleware, which keeps the original status on the response.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int id) => View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, StatusCode = id });
}
