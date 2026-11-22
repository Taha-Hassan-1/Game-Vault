using GameVault.Application.Catalogue;
using GameVault.Application.Common;
using GameVault.Domain.Entities;

namespace GameVault.Application.Studio;

/// <summary>Recognises an image by its first bytes, never by the name or type the browser claims. SVG is excluded on purpose: it can carry script.</summary>
public static class ImageSniffer
{
    public static string? Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }
}

public sealed class StudioGameService
{
    public const int MaxTitle = 300;
    public const int MaxDescription = 8000;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const string UploadsPrefix = "/uploads/";

    private readonly IStudioGameRepository _games;
    private readonly IImageStore _images;
    private readonly IGameCatalog _catalog;

    public StudioGameService(IStudioGameRepository games, IImageStore images, IGameCatalog catalog)
    {
        _games = games;
        _images = images;
        _catalog = catalog;
    }

    public Task<IReadOnlyList<StudioGameRow>> ListAsync(CancellationToken ct = default) => _games.ListAsync(ct);
    public Task<StudioGameEdit?> GetAsync(int id, CancellationToken ct = default) => _games.GetAsync(id, ct);

    public static string? Validate(StudioGameDraft d)
    {
        if (string.IsNullOrWhiteSpace(d.Title)) return "Give the game a title.";
        if (d.Title.Trim().Length > MaxTitle) return $"The title can be at most {MaxTitle} characters.";
        if ((d.Description ?? "").Length > MaxDescription) return $"The description can be at most {MaxDescription} characters.";
        if (!string.IsNullOrWhiteSpace(d.CoverUrl) && !IsAllowedCover(d.CoverUrl.Trim()))
            return "The cover must be an http(s) address or an uploaded image.";
        return null;
    }

    /// <summary>Covers shipped with the site (seeded studio games). Allowed so that editing such a game keeps its cover.</summary>
    public const string BuiltInCoverPrefix = "/img/studio/";

    private static bool IsAllowedCover(string url)
        => url.Length <= 500 && (url.StartsWith(UploadsPrefix, StringComparison.Ordinal)
            || (url.StartsWith(BuiltInCoverPrefix, StringComparison.Ordinal) && !url.Contains(".."))
            || (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)));

    private static StudioGameDraft Clean(StudioGameDraft d) => d with
    {
        Title = d.Title.Trim(),
        Description = (d.Description ?? "").Trim(),
        CoverUrl = string.IsNullOrWhiteSpace(d.CoverUrl) ? null : d.CoverUrl.Trim(),
        GenreSlugs = d.GenreSlugs.Distinct().ToList(),
        PlatformSlugs = d.PlatformSlugs.Distinct().ToList(),
        TagSlugs = d.TagSlugs.Distinct().ToList()
    };

    /// <summary>The slug is generated once here and never changes afterwards, so links to a game stay valid.</summary>
    public async Task<OpResult<int>> CreateAsync(StudioGameDraft draft, CancellationToken ct = default)
    {
        if (Validate(draft) is { } error) return OpResult<int>.Fail(error);
        draft = Clean(draft);

        var baseSlug = Common.Slug.From(draft.Title);
        var slug = baseSlug;
        for (var n = 2; await _games.SlugExistsAsync(slug, ct); n++) slug = $"{baseSlug}-{n}";

        var id = await _games.CreateAsync(slug, draft, ct);
        await _catalog.InvalidateAsync(ct);
        return OpResult<int>.Success(id);
    }

    public async Task<OpResult> UpdateAsync(int id, StudioGameDraft draft, CancellationToken ct = default)
    {
        if (Validate(draft) is { } error) return OpResult.Fail(error);
        if (!await _games.UpdateAsync(id, Clean(draft), ct)) return OpResult.Fail("That studio game does not exist.");
        await _catalog.InvalidateAsync(ct);
        return OpResult.Success;
    }

    public async Task<OpResult> AddScreenshotAsync(int gameId, Stream file, CancellationToken ct = default)
    {
        if (!await _games.IsStudioGameAsync(gameId, ct)) return OpResult.Fail("That studio game does not exist.");
        if (await _games.CountScreenshotsAsync(gameId, ct) >= Game.MaxScreenshots)
            return OpResult.Fail($"A game can have at most {Game.MaxScreenshots} screenshots.");

        var saved = await StoreAsync(file, ct);
        if (!saved.Ok) return OpResult.Fail(saved.Error!);

        await _games.AddScreenshotAsync(gameId, saved.Value!, ct);
        await _catalog.InvalidateAsync(ct);
        return OpResult.Success;
    }

    public async Task<OpResult> RemoveScreenshotAsync(int gameId, int screenshotId, CancellationToken ct = default)
    {
        var url = await _games.RemoveScreenshotAsync(gameId, screenshotId, ct);
        if (url is null) return OpResult.Fail("That screenshot does not exist.");
        await _images.DeleteAsync(url, ct);
        await _catalog.InvalidateAsync(ct);
        return OpResult.Success;
    }

    public async Task<OpResult> UploadCoverAsync(int gameId, Stream file, CancellationToken ct = default)
    {
        if (!await _games.IsStudioGameAsync(gameId, ct)) return OpResult.Fail("That studio game does not exist.");
        var saved = await StoreAsync(file, ct);
        if (!saved.Ok) return OpResult.Fail(saved.Error!);

        var previous = await _games.SetCoverAsync(gameId, saved.Value!, ct);
        if (previous is not null) await _images.DeleteAsync(previous, ct);
        await _catalog.InvalidateAsync(ct);
        return OpResult.Success;
    }

    private async Task<OpResult<string>> StoreAsync(Stream file, CancellationToken ct)
    {
        // Read one byte past the limit so an oversized upload is detected without trusting any declared length.
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await file.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxImageBytes) return OpResult<string>.Fail("The image is larger than 2 MB.");
        }
        if (buffer.Length == 0) return OpResult<string>.Fail("Choose an image to upload.");

        var bytes = buffer.ToArray();
        var ext = ImageSniffer.Detect(bytes);
        if (ext is null) return OpResult<string>.Fail("Only PNG, JPEG and WebP images are accepted.");
        return OpResult<string>.Success(await _images.SaveAsync(bytes, ext, ct));
    }
}
