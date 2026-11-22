namespace GameVault.Web.Models;

/// <summary>
/// RAWG serves covers and screenshots at full size (often over a megabyte). A grid card only needs a few hundred pixels,
/// and RAWG's CDN resizes on request: /media/games/ab/abc.jpg becomes /media/resize/420/-/games/ab/abc.jpg.
/// Anything that is not a plain RAWG media URL (studio uploads, other hosts, already resized) is returned unchanged.
/// The page keeps the original in data-fallback-src and site.js swaps back to it if the resized image fails to load,
/// so a change on RAWG's side costs bandwidth, never a broken picture.
/// </summary>
public static class ImageUrls
{
    public const int Card = 420;
    public const int Cover = 640;
    private const string Host = "media.rawg.io";
    private const string MediaPrefix = "/media/";
    private const string ResizeMarker = "/media/resize/";
    private const string CropMarker = "/media/crop/";

    public static string? Resized(string? url, int width)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) return url;

        var path = uri.AbsolutePath;
        if (!path.StartsWith(MediaPrefix, StringComparison.Ordinal)
            || path.StartsWith(ResizeMarker, StringComparison.Ordinal) || path.StartsWith(CropMarker, StringComparison.Ordinal)) return url;

        return $"https://{Host}{ResizeMarker}{width}/-/{path[MediaPrefix.Length..]}{uri.Query}";
    }

    /// <summary>The original, only when it differs from the resized address, for the data-fallback-src attribute.</summary>
    public static string? FallbackFor(string? original, int width)
        => Resized(original, width) is { } resized && resized != original ? original : null;
}
