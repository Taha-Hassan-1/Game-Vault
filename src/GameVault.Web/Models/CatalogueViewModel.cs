using GameVault.Application.Catalogue;

namespace GameVault.Web.Models;

public sealed record CatalogueViewModel(CatalogueQuery Query, PagedResult<GameCard> Result, CatalogueFacets Facets)
{
    /// <summary>Route values for a link to another page that keeps the active filters.</summary>
    public Dictionary<string, string?> PageRoute(int page) => new()
    {
        ["search"] = Query.Search,
        ["genre"] = Query.Genre,
        ["platform"] = Query.Platform,
        ["tag"] = Query.Tag,
        ["sort"] = Query.Sort.ToString(),
        ["page"] = page.ToString()
    };
}
