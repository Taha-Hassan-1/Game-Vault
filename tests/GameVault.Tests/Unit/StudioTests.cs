using GameVault.Application.Catalogue;
using GameVault.Application.Studio;
using GameVault.Domain.Entities;
using GameVault.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GameVault.Tests.Unit;

public class ImageSnifferTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
    private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    [Fact] public void Png_is_recognised() => Assert.Equal(".png", ImageSniffer.Detect(Png));
    [Fact] public void Jpeg_is_recognised() => Assert.Equal(".jpg", ImageSniffer.Detect(Jpg));
    [Fact] public void Webp_is_recognised() => Assert.Equal(".webp", ImageSniffer.Detect(Webp));

    [Theory]
    [InlineData("GIF89a")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("MZ this is an executable")]
    [InlineData("")]
    public void Anything_else_is_refused_whatever_it_is_named(string text)
        => Assert.Null(ImageSniffer.Detect(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_truncated_header_is_refused()
    {
        Assert.Null(ImageSniffer.Detect(Png.AsSpan(0, 5)));
        Assert.Null(ImageSniffer.Detect(Webp.AsSpan(0, 10)));
    }
}

public class StudioGameServiceTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    private readonly IStudioGameRepository _repo = Substitute.For<IStudioGameRepository>();
    private readonly IImageStore _images = Substitute.For<IImageStore>();
    private readonly IGameCatalog _catalog = Substitute.For<IGameCatalog>();
    private readonly StudioGameService _service;

    public StudioGameServiceTests()
    {
        _repo.IsStudioGameAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _images.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci => "/uploads/new" + ci.ArgAt<string>(1));
        _service = new StudioGameService(_repo, _images, _catalog);
    }

    private static StudioGameDraft Draft(string title = "My Game", string? description = "d", string? cover = null)
        => new(title, description ?? "", null, cover, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_title_is_required(string title) => Assert.NotNull(StudioGameService.Validate(Draft(title)));

    [Fact]
    public void Over_long_text_is_refused()
    {
        Assert.NotNull(StudioGameService.Validate(Draft(new string('a', 301))));
        Assert.NotNull(StudioGameService.Validate(Draft(description: new string('a', 8001))));
    }

    [Theory]
    [InlineData("https://example.com/cover.png", true)]
    [InlineData("http://example.com/cover.png", true)]
    [InlineData("/uploads/abc.png", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("//evil.example/x.png", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("ftp://example.com/x.png", false)]
    public void Only_web_addresses_and_our_own_uploads_are_accepted_as_covers(string cover, bool allowed)
        => Assert.Equal(allowed, StudioGameService.Validate(Draft(cover: cover)) is null);

    [Fact]
    public async Task The_slug_comes_from_the_title_and_gets_a_number_when_taken()
    {
        _repo.SlugExistsAsync("my-game", Arg.Any<CancellationToken>()).Returns(true);
        _repo.SlugExistsAsync("my-game-2", Arg.Any<CancellationToken>()).Returns(true);
        _repo.CreateAsync("my-game-3", Arg.Any<StudioGameDraft>(), Arg.Any<CancellationToken>()).Returns(9);

        var r = await _service.CreateAsync(Draft("My Game!"));

        Assert.True(r.Ok);
        Assert.Equal(9, r.Value);
        await _catalog.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_draft_is_never_stored()
    {
        Assert.False((await _service.CreateAsync(Draft(""))).Ok);
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!);
    }

    [Fact]
    public async Task Update_trims_and_dedupes_and_reports_a_missing_game()
    {
        _repo.UpdateAsync(5, Arg.Any<StudioGameDraft>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.False((await _service.UpdateAsync(5, Draft())).Ok);

        _repo.UpdateAsync(1, Arg.Any<StudioGameDraft>(), Arg.Any<CancellationToken>()).Returns(true);
        var draft = Draft("  Padded  ") with { GenreSlugs = new[] { "a", "a", "b" } };
        Assert.True((await _service.UpdateAsync(1, draft)).Ok);
        await _repo.Received(1).UpdateAsync(1,
            Arg.Is<StudioGameDraft>(d => d.Title == "Padded" && d.GenreSlugs.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_valid_screenshot_is_stored_by_its_content_not_its_name()
    {
        var r = await _service.AddScreenshotAsync(1, new MemoryStream(Png));

        Assert.True(r.Ok);
        await _images.Received(1).SaveAsync(Arg.Any<byte[]>(), ".png", Arg.Any<CancellationToken>());
        await _repo.Received(1).AddScreenshotAsync(1, "/uploads/new.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Non_images_are_refused_and_nothing_is_written()
    {
        var r = await _service.AddScreenshotAsync(1, new MemoryStream("<script>alert(1)</script>"u8.ToArray()));

        Assert.False(r.Ok);
        await _images.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
        await _repo.DidNotReceiveWithAnyArgs().AddScreenshotAsync(default, default!);
    }

    [Fact]
    public async Task An_image_over_2_MB_is_refused()
    {
        var big = new byte[StudioGameService.MaxImageBytes + 1];
        Png.CopyTo(big, 0);

        Assert.False((await _service.AddScreenshotAsync(1, new MemoryStream(big))).Ok);
        await _images.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
        => Assert.False((await _service.AddScreenshotAsync(1, new MemoryStream())).Ok);

    [Fact]
    public async Task The_ninth_screenshot_is_refused()
    {
        _repo.CountScreenshotsAsync(1, Arg.Any<CancellationToken>()).Returns(Game.MaxScreenshots);
        Assert.False((await _service.AddScreenshotAsync(1, new MemoryStream(Png))).Ok);
    }

    [Fact]
    public async Task Uploads_for_a_game_that_is_not_a_studio_game_are_refused()
        => Assert.False((await _service.AddScreenshotAsync(2, new MemoryStream(Png))).Ok);

    [Fact]
    public async Task Removing_a_screenshot_deletes_its_file()
    {
        _repo.RemoveScreenshotAsync(1, 4, Arg.Any<CancellationToken>()).Returns("/uploads/x.png");

        Assert.True((await _service.RemoveScreenshotAsync(1, 4)).Ok);

        await _images.Received(1).DeleteAsync("/uploads/x.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replacing_the_cover_deletes_the_previous_file()
    {
        _repo.SetCoverAsync(1, "/uploads/new.png", Arg.Any<CancellationToken>()).Returns("/uploads/old.png");

        Assert.True((await _service.UploadCoverAsync(1, new MemoryStream(Png))).Ok);

        await _images.Received(1).DeleteAsync("/uploads/old.png", Arg.Any<CancellationToken>());
    }
}

public sealed class FileImageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gv_store_" + Guid.NewGuid().ToString("N"));
    private readonly FileImageStore _store;

    public FileImageStoreTests()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_dir);
        _store = new FileImageStore(Options.Create(new UploadsOptions { Path = _dir }), env);
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public async Task Saving_generates_the_name_and_returns_an_uploads_url()
    {
        var url = await _store.SaveAsync(new byte[] { 1, 2, 3 }, ".png");

        Assert.StartsWith("/uploads/", url);
        Assert.EndsWith(".png", url);
        Assert.True(File.Exists(Path.Combine(_dir, url["/uploads/".Length..])));
        Assert.NotEqual(url, await _store.SaveAsync(new byte[] { 1 }, ".png"));
    }

    [Fact]
    public async Task Deleting_removes_our_file()
    {
        var url = await _store.SaveAsync(new byte[] { 1 }, ".jpg");
        await _store.DeleteAsync(url);
        Assert.False(File.Exists(Path.Combine(_dir, url["/uploads/".Length..])));
    }

    [Theory]
    [InlineData("https://example.com/uploads/a.png")]
    [InlineData("/uploads/../outside.txt")]
    [InlineData("/uploads/sub/../../outside.txt")]
    [InlineData("/uploads/")]
    [InlineData("/other/a.png")]
    [InlineData("/uploads/..\\outside.txt")]
    public async Task Deleting_anything_that_is_not_a_plain_upload_name_does_nothing(string url)
    {
        var outside = Path.Combine(Path.GetDirectoryName(_dir)!, "outside.txt");
        await File.WriteAllTextAsync(outside, "keep");
        try
        {
            await _store.DeleteAsync(url);
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); }
    }
}
