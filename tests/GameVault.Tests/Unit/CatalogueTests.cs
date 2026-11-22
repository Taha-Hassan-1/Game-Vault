using GameVault.Application.Catalogue;
using GameVault.Domain.Entities;
using NSubstitute;

namespace GameVault.Tests.Unit;

public class CatalogueTests
{
    private static GameCard Card(int id, string slug, double rating = 0) =>
        new(id, slug, slug, null, null, rating, 0, false, Array.Empty<string>());

    private static SimilarityCandidate Cand(int id, string slug, string[] genres, string[] tags, double rating = 0) =>
        new(Card(id, slug, rating), genres.ToHashSet(), tags.ToHashSet());

    [Fact]
    public void Query_normalisation_clamps_page_size_and_trims_filters()
    {
        var n = new CatalogueQuery("  hades ", " Puzzle ", "", null, GameSort.Title, -3, 500).Normalized();

        Assert.Equal("hades", n.Search);
        Assert.Equal("puzzle", n.Genre);
        Assert.Null(n.Platform);
        Assert.Equal(1, n.Page);
        Assert.Equal(CatalogueQuery.MaxPageSize, n.PageSize);
    }

    [Fact]
    public void Blank_search_becomes_null()
        => Assert.Null(new CatalogueQuery("   ").Normalized().Search);

    [Theory]
    [InlineData(0, 12, 0)]
    [InlineData(1, 12, 1)]
    [InlineData(25, 12, 3)]
    [InlineData(24, 12, 2)]
    public void Total_pages_rounds_up(int total, int size, int pages)
        => Assert.Equal(pages, new PagedResult<int>(Array.Empty<int>(), 1, size, total).TotalPages);

    [Fact]
    public void Identical_taxonomy_scores_one()
    {
        var a = Cand(1, "a", new[] { "puzzle" }, new[] { "casual" });
        var b = Cand(2, "b", new[] { "puzzle" }, new[] { "casual" });
        Assert.Equal(1.0, new TagOverlapSimilarity().Score(a, b), 6);
    }

    [Fact]
    public void Disjoint_taxonomy_scores_zero_and_empty_sets_do_not_divide_by_zero()
    {
        var s = new TagOverlapSimilarity();
        Assert.Equal(0, s.Score(Cand(1, "a", new[] { "puzzle" }, new[] { "x" }), Cand(2, "b", new[] { "rpg" }, new[] { "y" })));
        Assert.Equal(0, s.Score(Cand(1, "a", Array.Empty<string>(), Array.Empty<string>()), Cand(2, "b", Array.Empty<string>(), Array.Empty<string>())));
    }

    [Fact]
    public void Genres_weigh_more_than_tags()
    {
        var s = new TagOverlapSimilarity();
        var source = Cand(1, "src", new[] { "puzzle" }, new[] { "casual" });
        var sameGenre = Cand(2, "g", new[] { "puzzle" }, new[] { "other" });
        var sameTag = Cand(3, "t", new[] { "rpg" }, new[] { "casual" });
        Assert.True(s.Score(source, sameGenre) > s.Score(source, sameTag));
    }

    [Fact]
    public async Task Similar_games_are_ranked_filtered_and_limited()
    {
        var repo = Substitute.For<IGameRepository>();
        var source = Cand(1, "src", new[] { "puzzle" }, new[] { "casual" });
        repo.GetCandidateAsync("src", Arg.Any<CancellationToken>()).Returns(source);
        repo.GetCandidatePoolAsync("src", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Cand(2, "weak", new[] { "puzzle", "rpg", "action" }, Array.Empty<string>()),
            Cand(3, "best", new[] { "puzzle" }, new[] { "casual" }),
            Cand(4, "unrelated", new[] { "racing" }, new[] { "fast" }),
        });

        var result = await new GameCatalog(repo, new TagOverlapSimilarity()).GetSimilarAsync("SRC", 5);

        Assert.Equal(new[] { "best", "weak" }, result.Select(c => c.Slug));
        Assert.Single(await new GameCatalog(repo, new TagOverlapSimilarity()).GetSimilarAsync("src", 1));
    }

    [Fact]
    public async Task Similar_games_for_unknown_slug_is_empty()
    {
        var repo = Substitute.For<IGameRepository>();
        repo.GetCandidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SimilarityCandidate?)null);
        Assert.Empty(await new GameCatalog(repo, new TagOverlapSimilarity()).GetSimilarAsync("x", 3));
    }

    [Fact]
    public async Task Search_passes_a_normalised_query_to_the_repository()
    {
        var repo = Substitute.For<IGameRepository>();
        repo.SearchAsync(Arg.Any<CatalogueQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<GameCard>(Array.Empty<GameCard>(), 1, 24, 0));

        await new GameCatalog(repo, new TagOverlapSimilarity()).SearchAsync(new CatalogueQuery(PageSize: 999));

        await repo.Received().SearchAsync(Arg.Is<CatalogueQuery>(q => q.PageSize == 24), Arg.Any<CancellationToken>());
    }
}

public class DomainRuleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Review_rejects_stars_outside_1_to_5(int stars)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Review().Write(stars, "x"));

    [Fact]
    public void Review_trims_body()
    {
        var r = new Review();
        r.Write(4, "  great  ");
        Assert.Equal("great", r.Body);
        Assert.Equal(4, r.Stars);
    }

    [Fact]
    public void Game_rating_is_zero_without_reviews_and_rounded_otherwise()
    {
        var g = new Game();
        g.ApplyRating(0, 3.5);
        Assert.Equal(0, g.RatingAverage);
        g.ApplyRating(3, 4.33333);
        Assert.Equal(4.33, g.RatingAverage);
    }

    [Fact]
    public void Game_allows_at_most_eight_screenshots()
    {
        var g = new Game();
        for (var i = 0; i < Game.MaxScreenshots; i++) g.Screenshots.Add(new Screenshot());
        Assert.False(g.CanAddScreenshot());
    }
}
