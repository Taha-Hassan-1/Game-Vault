using System.Text;
using GameVault.Application.Catalogue;
using GameVault.Application.Feedback;
using GameVault.Application.Studio;
using GameVault.Domain;
using GameVault.Web.Models;
using GameVault.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Controllers;

[Authorize(Policy = Policies.StudioOwner)]
[Route("studio")]
public class StudioController : Controller
{
    private readonly FeedbackService _feedback;
    public StudioController(FeedbackService feedback) => _feedback = feedback;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _feedback.GetDashboardAsync(ct));
}

[Authorize(Policy = Policies.StudioOwner)]
[Route("studio/games")]
public class StudioGamesController : Controller
{
    private const int UploadLimit = StudioGameService.MaxImageBytes + 64 * 1024; // image plus multipart overhead

    private readonly StudioGameService _games;
    private readonly IGameCatalog _catalog;

    public StudioGamesController(StudioGameService games, IGameCatalog catalog)
    {
        _games = games;
        _catalog = catalog;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _games.ListAsync(ct));

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken ct)
        => View("Edit", new StudioGameEditViewModel(new StudioGameForm(), await _catalog.GetFacetsAsync(ct), null, Array.Empty<ScreenshotItem>()));

    [HttpPost("new")]
    public async Task<IActionResult> New(StudioGameForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await _games.CreateAsync(form.ToDraft(), ct);
            if (result.Ok)
            {
                TempData["Message"] = "Game created. Add a cover and screenshots below.";
                return RedirectToAction(nameof(Edit), new { id = result.Value });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return View("Edit", new StudioGameEditViewModel(form, await _catalog.GetFacetsAsync(ct), null, Array.Empty<ScreenshotItem>()));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var game = await _games.GetAsync(id, ct);
        if (game is null) return NotFound();
        return View(new StudioGameEditViewModel(StudioGameForm.From(game), await _catalog.GetFacetsAsync(ct), game.Slug, game.Screenshots));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> Edit(int id, StudioGameForm form, CancellationToken ct)
    {
        var game = await _games.GetAsync(id, ct);
        if (game is null) return NotFound();
        form.Id = id;

        if (ModelState.IsValid)
        {
            var result = await _games.UpdateAsync(id, form.ToDraft(), ct);
            if (result.Ok)
            {
                TempData["Message"] = "Saved.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return View(new StudioGameEditViewModel(form, await _catalog.GetFacetsAsync(ct), game.Slug, game.Screenshots));
    }

    [HttpPost("{id:int}/screenshots")]
    [RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public Task<IActionResult> AddScreenshot(int id, IFormFile? file, CancellationToken ct)
        => Upload(id, file, (stream, c) => _games.AddScreenshotAsync(id, stream, c), "Screenshot added.", ct);

    [HttpPost("{id:int}/cover")]
    [RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public Task<IActionResult> UploadCover(int id, IFormFile? file, CancellationToken ct)
        => Upload(id, file, (stream, c) => _games.UploadCoverAsync(id, stream, c), "Cover updated.", ct);

    [HttpPost("{id:int}/screenshots/{shotId:int}/delete")]
    public async Task<IActionResult> RemoveScreenshot(int id, int shotId, CancellationToken ct)
    {
        var result = await _games.RemoveScreenshotAsync(id, shotId, ct);
        TempData["Message"] = result.Ok ? "Screenshot removed." : result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<IActionResult> Upload(
        int id, IFormFile? file, Func<Stream, CancellationToken, Task<GameVault.Application.Common.OpResult>> save, string success, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Message"] = "Choose an image to upload.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        await using var stream = file.OpenReadStream();
        var result = await save(stream, ct);
        TempData["Message"] = result.Ok ? success : result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }
}

[Authorize(Policy = Policies.StudioOwner)]
[Route("studio/inbox")]
public class StudioInboxController : Controller
{
    private readonly FeedbackService _feedback;
    private readonly StudioGameService _games;

    public StudioInboxController(FeedbackService feedback, StudioGameService games)
    {
        _feedback = feedback;
        _games = games;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] FeedbackFilter filter, CancellationToken ct)
    {
        var f = filter.Normalized();
        return View(new InboxViewModel(f, await _feedback.ListAsync(f, ct), await _games.ListAsync(ct)));
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, string status, string? returnUrl, CancellationToken ct)
    {
        if (!Enum.TryParse<FeedbackStatus>(status, ignoreCase: true, out var s) || !await _feedback.SetStatusAsync(id, s, ct))
            return NotFound();
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> Export([FromQuery] FeedbackFilter filter, CancellationToken ct)
    {
        var csv = await _feedback.ExportCsvAsync(filter, ct);
        // A byte-order mark makes Excel read the file as UTF-8.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"gamevault-feedback-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

[Authorize(Policy = Policies.StudioOwner)]
[Route("studio/keys")]
public class StudioKeysController : Controller
{
    private readonly ApiKeyService _keys;
    private readonly StudioGameService _games;

    public StudioKeysController(ApiKeyService keys, StudioGameService games)
    {
        _keys = keys;
        _games = games;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new KeysViewModel(await _keys.ListAsync(ct), await _games.ListAsync(ct)));

    /// <summary>Shows the key on this response only; it is never stored in plain text and cannot be shown again.</summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(int gameId, CancellationToken ct)
    {
        var result = await _keys.CreateAsync(gameId, ct);
        if (!result.Ok)
        {
            TempData["Message"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        var game = (await _games.ListAsync(ct)).First(g => g.Id == gameId);
        Response.Headers.CacheControl = "no-store";
        return View("Created", new NewKeyViewModel(game.Title, result.Value!));
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var result = await _keys.RevokeAsync(id, ct);
        TempData["Message"] = result.Ok ? "Key revoked. Builds using it will be rejected." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
