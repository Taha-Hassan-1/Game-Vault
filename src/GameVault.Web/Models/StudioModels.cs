using System.ComponentModel.DataAnnotations;
using GameVault.Application.Catalogue;
using GameVault.Application.Feedback;
using GameVault.Application.Studio;
using GameVault.Application.Suggestions;
using GameVault.Domain;

namespace GameVault.Web.Models;

public class StudioGameForm
{
    public int Id { get; set; }

    [Required, StringLength(StudioGameService.MaxTitle)]
    public string Title { get; set; } = "";

    [StringLength(StudioGameService.MaxDescription)]
    public string? Description { get; set; }

    [DataType(DataType.Date), Display(Name = "Release date")]
    public DateOnly? ReleaseDate { get; set; }

    [StringLength(500), Display(Name = "Cover address")]
    public string? CoverUrl { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public StudioGameDraft ToDraft() => new(Title, Description ?? "", ReleaseDate, CoverUrl, Genres, Platforms, Tags);

    public static StudioGameForm From(StudioGameEdit e) => new()
    {
        Id = e.Id, Title = e.Draft.Title, Description = e.Draft.Description, ReleaseDate = e.Draft.ReleaseDate,
        CoverUrl = e.Draft.CoverUrl, Genres = e.Draft.GenreSlugs.ToList(), Platforms = e.Draft.PlatformSlugs.ToList(), Tags = e.Draft.TagSlugs.ToList()
    };
}

public sealed record StudioGameEditViewModel(
    StudioGameForm Form, CatalogueFacets Facets, string? Slug, IReadOnlyList<ScreenshotItem> Screenshots)
{
    public bool IsNew => Form.Id == 0;
}

public sealed record InboxViewModel(FeedbackFilter Filter, PagedResult<FeedbackItem> Result, IReadOnlyList<StudioGameRow> Games)
{
    public Dictionary<string, string?> PageRoute(int page) => new()
    {
        ["gameId"] = Filter.GameId?.ToString(),
        ["category"] = Filter.Category?.ToString(),
        ["status"] = Filter.Status?.ToString(),
        ["source"] = Filter.Source?.ToString(),
        ["page"] = page.ToString()
    };
}

public sealed record KeysViewModel(IReadOnlyList<ApiKeyRow> Keys, IReadOnlyList<StudioGameRow> Games);

public sealed record NewKeyViewModel(string GameTitle, NewApiKey Key);

public class SuggestionForm
{
    [Required, StringLength(SuggestionService.MaxTitle, MinimumLength = SuggestionService.MinTitle)]
    public string Title { get; set; } = "";

    [StringLength(SuggestionService.MaxBody)]
    public string? Body { get; set; }
}

public sealed record SuggestionBoardViewModel(
    SuggestionGame Game, IReadOnlyList<SuggestionItem> Items, SuggestionStatus? Filter, bool IsSignedIn, bool IsOwner, SuggestionForm Form);

public class FeedbackForm
{
    [Required]
    public string Category { get; set; } = nameof(FeedbackCategory.Idea);

    [Required, StringLength(FeedbackService.MaxBody)]
    public string Message { get; set; } = "";

    public string Slug { get; set; } = "";
    public string GameTitle { get; set; } = "";
}
