using System.Globalization;
using GameVault.Application.Metadata;
using GameVault.Domain.Entities;

namespace GameVault.Infrastructure.Rawg;

/// <summary>The one place RAWG's JSON shape is turned into the app's own types.</summary>
internal static class RawgMapper
{
    private const int MaxTags = 10;

    public static ExternalGame? ToExternal(RawgGameDto dto, IReadOnlyList<string>? screenshots = null)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return null;
        var detailed = dto.DescriptionRaw is not null;
        var shots = screenshots
                    ?? dto.ShortScreenshots?.Select(s => s.Image).OfType<string>().ToList()
                    ?? new List<string>();

        return new ExternalGame(
            dto.Id.ToString(CultureInfo.InvariantCulture),
            Slug(dto.Slug, dto.Name),
            Clip(dto.Name, 300),
            detailed ? dto.DescriptionRaw!.Trim() : null,
            ParseDate(dto.Released),
            dto.BackgroundImage,
            Taxa(dto.Genres),
            Taxa(dto.Platforms?.Select(p => p.Platform)),
            Taxa(dto.Tags).Take(MaxTags).ToList(),
            shots.Take(Game.MaxScreenshots).ToList(),
            detailed);
    }

    private static string Slug(string? slug, string name)
        => Clip(string.IsNullOrWhiteSpace(slug) ? name.ToLowerInvariant().Replace(' ', '-') : slug.ToLowerInvariant(), 200);

    private static DateOnly? ParseDate(string? s)
        => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static List<ExternalTaxon> Taxa(IEnumerable<RawgTaxonDto?>? items)
        => items?.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Slug))
               .Select(t => new ExternalTaxon(Clip(t!.Name!, 100), Clip(t.Slug!.ToLowerInvariant(), 100)))
               .DistinctBy(t => t.Slug).ToList()
           ?? new List<ExternalTaxon>();

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
