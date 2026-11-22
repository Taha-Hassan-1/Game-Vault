using GameVault.Application.Lists;
using GameVault.Domain;
using GameVault.Web.Models;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

[Authorize]
public class ListsController : Controller
{
    private readonly ListService _lists;
    public ListsController(ListService lists) => _lists = lists;

    [HttpGet("me/lists")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var id = User.UserId()!;
        return View(new ListsViewModel(
            await _lists.GetAsync(id, ListKind.Wishlist, ct),
            await _lists.GetAsync(id, ListKind.Played, ct)));
    }

    [HttpPost("games/{slug}/lists/{kind}")]
    public async Task<IActionResult> Set(string slug, string kind, bool present, string? returnUrl, CancellationToken ct)
    {
        if (!Enum.TryParse<ListKind>(kind, ignoreCase: true, out var k)) return NotFound();

        var result = await _lists.SetAsync(User.UserId()!, slug, k, present, ct);
        if (!result.Ok) return NotFound();
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToRoute("game", new { slug });
    }
}
