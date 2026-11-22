namespace GameVault.Infrastructure.Rawg;

// Wire shapes only. Names map from RAWG's snake_case JSON; nothing outside this folder sees these types.
internal sealed record RawgListResponse(int Count, string? Next, List<RawgGameDto>? Results);

internal sealed record RawgGameDto(
    int Id,
    string? Slug,
    string? Name,
    string? Released,
    string? BackgroundImage,
    string? DescriptionRaw,
    List<RawgTaxonDto>? Genres,
    List<RawgPlatformWrapperDto>? Platforms,
    List<RawgTaxonDto>? Tags,
    List<RawgImageDto>? ShortScreenshots);

internal sealed record RawgTaxonDto(string? Name, string? Slug);
internal sealed record RawgPlatformWrapperDto(RawgTaxonDto? Platform);
internal sealed record RawgImageDto(string? Image);
internal sealed record RawgScreenshotsResponse(List<RawgImageDto>? Results);
