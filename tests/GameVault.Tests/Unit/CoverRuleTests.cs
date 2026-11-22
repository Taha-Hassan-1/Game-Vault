using GameVault.Application.Studio;

namespace GameVault.Tests.Unit;

public class CoverRuleTests
{
    private static StudioGameDraft Draft(string? cover) =>
        new("Title", "Text", null, cover, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    [Theory]
    [InlineData("/img/studio/untangle.webp")]
    [InlineData("/uploads/abc.png")]
    [InlineData("https://example.com/cover.png")]
    [InlineData(null)]
    public void These_covers_are_accepted(string? cover) => Assert.Null(StudioGameService.Validate(Draft(cover)));

    [Theory]
    [InlineData("/img/studio/../../etc/passwd")]
    [InlineData("/img/other/x.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/x.png")]
    public void These_covers_are_refused(string cover) => Assert.NotNull(StudioGameService.Validate(Draft(cover)));
}
