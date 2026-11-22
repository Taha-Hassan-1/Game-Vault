using GameVault.Application.Reviews;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

[Authorize(Policy = Policies.CanModerate)]
[Route("moderation")]
public class ModerationController : Controller
{
    private readonly ModerationService _moderation;
    public ModerationController(ModerationService moderation) => _moderation = moderation;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _moderation.GetQueueAsync(ct));

    [HttpPost("{id:int}/hide")]
    public Task<IActionResult> Hide(int id, CancellationToken ct) => Act(() => _moderation.SetHiddenAsync(id, true, ct), "Review hidden.");

    [HttpPost("{id:int}/restore")]
    public Task<IActionResult> Restore(int id, CancellationToken ct) => Act(() => _moderation.SetHiddenAsync(id, false, ct), "Review restored.");

    [HttpPost("{id:int}/dismiss")]
    public Task<IActionResult> Dismiss(int id, CancellationToken ct) => Act(() => _moderation.DismissReportsAsync(id, ct), "Reports dismissed.");

    private async Task<IActionResult> Act(Func<Task<GameVault.Application.Common.OpResult>> action, string success)
    {
        var result = await action();
        TempData["Message"] = result.Ok ? success : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
