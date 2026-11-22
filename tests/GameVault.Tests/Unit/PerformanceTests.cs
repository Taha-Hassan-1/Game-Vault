using GameVault.Web.Models;

namespace GameVault.Tests.Unit;

public class ImageUrlTests
{
    [Theory]
    [InlineData("https://media.rawg.io/media/games/456/456dea5e1c7e3cd07060c14e96612001.jpg",
                "https://media.rawg.io/media/resize/420/-/games/456/456dea5e1c7e3cd07060c14e96612001.jpg")]
    [InlineData("https://media.rawg.io/media/screenshots/abc/abcdef.jpg",
                "https://media.rawg.io/media/resize/420/-/screenshots/abc/abcdef.jpg")]
    [InlineData("https://MEDIA.RAWG.IO/media/games/1/a.jpg?v=3", "https://media.rawg.io/media/resize/420/-/games/1/a.jpg?v=3")]
    public void Plain_rawg_media_urls_are_requested_at_card_size(string original, string expected)
        => Assert.Equal(expected, ImageUrls.Resized(original, ImageUrls.Card));

    [Fact]
    public void The_width_is_a_parameter()
        => Assert.Contains("/media/resize/640/-/", ImageUrls.Resized("https://media.rawg.io/media/games/1/a.jpg", ImageUrls.Cover));

    [Theory]
    [InlineData("https://media.rawg.io/media/resize/420/-/games/1/a.jpg")]       // already resized
    [InlineData("https://media.rawg.io/media/crop/600/400/games/1/a.jpg")]        // already cropped
    [InlineData("http://media.rawg.io/media/games/1/a.jpg")]                      // not https: never rewrite
    [InlineData("https://evil.example/media/games/1/a.jpg")]                      // another host
    [InlineData("https://media.rawg.io.evil.example/media/games/1/a.jpg")]        // look-alike host
    [InlineData("https://media.rawg.io/other/games/1/a.jpg")]                     // not under /media/
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.png")]                 // a studio upload
    [InlineData("not a url")]
    public void Anything_else_is_left_alone(string url) => Assert.Equal(url, ImageUrls.Resized(url, ImageUrls.Card));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_pass_through(string? url) => Assert.Equal(url, ImageUrls.Resized(url, ImageUrls.Card));

    [Fact]
    public void The_fallback_is_the_original_only_when_the_address_was_changed()
    {
        const string rawg = "https://media.rawg.io/media/games/1/a.jpg";
        Assert.Equal(rawg, ImageUrls.FallbackFor(rawg, ImageUrls.Card));
        Assert.Null(ImageUrls.FallbackFor("/uploads/x.png", ImageUrls.Card));
        Assert.Null(ImageUrls.FallbackFor(null, ImageUrls.Card));
    }
}
