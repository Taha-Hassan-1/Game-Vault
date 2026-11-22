using GameVault.Application.Catalogue;
using GameVault.Application.Events;
using GameVault.Application.Lists;
using GameVault.Application.Reviews;
using GameVault.Domain;
using GameVault.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GameVault.Tests.Unit;

public class ReviewServiceTests
{
    private readonly IReviewRepository _repo = Substitute.For<IReviewRepository>();
    private readonly IDomainEventDispatcher _events = Substitute.For<IDomainEventDispatcher>();
    private readonly ReviewService _service;

    public ReviewServiceTests()
    {
        _repo.GetGameIdAsync("game", Arg.Any<CancellationToken>()).Returns(7);
        _service = new ReviewService(_repo, _events, TimeProvider.System);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Stars_outside_1_to_5_are_refused_and_nothing_is_saved(int stars)
    {
        var r = await _service.SaveAsync("u", "game", stars, "x");

        Assert.False(r.Ok);
        await _repo.DidNotReceiveWithAnyArgs().SaveAsync();
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused()
        => Assert.False((await _service.SaveAsync("u", "game", 4, new string('a', ReviewService.MaxBodyLength + 1))).Ok);

    [Fact]
    public async Task A_review_of_an_unknown_game_is_refused()
        => Assert.False((await _service.SaveAsync("u", "nope", 4, "x")).Ok);

    [Fact]
    public async Task A_first_review_is_added_saved_and_announced()
    {
        _repo.GetByUserAsync(7, "u", Arg.Any<CancellationToken>()).Returns((Review?)null);

        var r = await _service.SaveAsync("u", "GAME", 5, "  loved it  ");

        Assert.True(r.Ok);
        _repo.Received(1).Add(Arg.Is<Review>(x => x.GameId == 7 && x.UserId == "u" && x.Stars == 5 && x.Body == "loved it"));
        await _repo.Received(1).SaveAsync(Arg.Any<CancellationToken>());
        await _events.Received(1).DispatchAsync(Arg.Is<ReviewChanged>(e => e.GameId == 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_second_submission_edits_the_existing_review_instead_of_adding_another()
    {
        var existing = new Review { Id = 1, GameId = 7, UserId = "u" };
        existing.Write(2, "meh");
        _repo.GetByUserAsync(7, "u", Arg.Any<CancellationToken>()).Returns(existing);

        await _service.SaveAsync("u", "game", 4, "better now");

        _repo.DidNotReceiveWithAnyArgs().Add(default!);
        Assert.Equal(4, existing.Stars);
        Assert.Equal("better now", existing.Body);
        Assert.NotNull(existing.EditedAt);
    }

    [Fact]
    public async Task Deleting_removes_the_review_and_announces_it()
    {
        var existing = new Review { Id = 1, GameId = 7, UserId = "u" };
        _repo.GetByUserAsync(7, "u", Arg.Any<CancellationToken>()).Returns(existing);

        Assert.True((await _service.DeleteMineAsync("u", "game")).Ok);

        _repo.Received(1).Remove(existing);
        await _events.Received(1).DispatchAsync(Arg.Any<ReviewChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_a_review_that_does_not_exist_fails()
    {
        _repo.GetByUserAsync(7, "u", Arg.Any<CancellationToken>()).Returns((Review?)null);
        Assert.False((await _service.DeleteMineAsync("u", "game")).Ok);
    }

    [Fact]
    public async Task Members_cannot_report_their_own_review()
    {
        _repo.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new Review { Id = 3, UserId = "me" });
        Assert.False((await _service.ReportAsync("me", 3, "spam")).Ok);
        await _repo.DidNotReceiveWithAnyArgs().AddReportAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_report_is_stored_once_per_member_and_defaults_the_reason()
    {
        _repo.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new Review { Id = 3, UserId = "author" });
        _repo.AddReportAsync(3, "me", "No reason given", Arg.Any<CancellationToken>()).Returns(true, false);

        Assert.True((await _service.ReportAsync("me", 3, "  ")).Ok);
        Assert.False((await _service.ReportAsync("me", 3, null)).Ok);
    }

    [Fact]
    public async Task Reporting_a_missing_review_fails()
    {
        _repo.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns((Review?)null);
        Assert.False((await _service.ReportAsync("me", 9, "x")).Ok);
    }
}

public class ModerationServiceTests
{
    private readonly IReviewRepository _repo = Substitute.For<IReviewRepository>();
    private readonly IDomainEventDispatcher _events = Substitute.For<IDomainEventDispatcher>();

    [Fact]
    public async Task Hiding_marks_the_review_clears_its_reports_and_recomputes_the_rating()
    {
        var review = new Review { Id = 5, GameId = 2 };
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(review);

        Assert.True((await new ModerationService(_repo, _events).SetHiddenAsync(5, true)).Ok);

        Assert.True(review.IsHidden);
        await _repo.Received(1).ClearReportsAsync(5, Arg.Any<CancellationToken>());
        await _events.Received(1).DispatchAsync(Arg.Is<ReviewChanged>(e => e.GameId == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restoring_unhides_without_touching_reports()
    {
        var review = new Review { Id = 5, GameId = 2, IsHidden = true };
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(review);

        await new ModerationService(_repo, _events).SetHiddenAsync(5, false);

        Assert.False(review.IsHidden);
        await _repo.DidNotReceiveWithAnyArgs().ClearReportsAsync(default);
        await _events.Received(1).DispatchAsync(Arg.Any<ReviewChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moderating_a_missing_review_fails_without_side_effects()
    {
        _repo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Review?)null);
        var service = new ModerationService(_repo, _events);

        Assert.False((await service.SetHiddenAsync(1, true)).Ok);
        Assert.False((await service.DismissReportsAsync(1)).Ok);
        await _events.DidNotReceiveWithAnyArgs().DispatchAsync<ReviewChanged>(default!);
    }

    [Fact]
    public async Task Dismissing_clears_reports_but_keeps_the_review_visible()
    {
        var review = new Review { Id = 5, GameId = 2 };
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(review);

        await new ModerationService(_repo, _events).DismissReportsAsync(5);

        Assert.False(review.IsHidden);
        await _repo.Received(1).ClearReportsAsync(5, Arg.Any<CancellationToken>());
    }
}

public class RatingRecalculatorTests
{
    [Fact]
    public async Task A_review_change_rewrites_the_game_rating_from_visible_reviews_and_clears_the_cache()
    {
        var repo = Substitute.For<IReviewRepository>();
        var catalog = Substitute.For<IGameCatalog>();
        repo.GetRatingSummaryAsync(4, Arg.Any<CancellationToken>()).Returns(new RatingSummary(3, 4.33));

        await new RatingRecalculator(repo, catalog).HandleAsync(new ReviewChanged(4));

        await repo.Received(1).UpdateGameRatingAsync(4, Arg.Is<RatingSummary>(s => s.Count == 3 && s.Average == 4.33), Arg.Any<CancellationToken>());
        await catalog.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dispatcher_calls_every_registered_handler()
    {
        var a = Substitute.For<IDomainEventHandler<ReviewChanged>>();
        var b = Substitute.For<IDomainEventHandler<ReviewChanged>>();
        var sp = new ServiceCollection().AddSingleton(a).AddSingleton(b).BuildServiceProvider();

        await new InProcessDispatcher(sp).DispatchAsync(new ReviewChanged(1));

        await a.Received(1).HandleAsync(Arg.Any<ReviewChanged>(), Arg.Any<CancellationToken>());
        await b.Received(1).HandleAsync(Arg.Any<ReviewChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dispatcher_is_fine_with_no_handlers()
        => await new InProcessDispatcher(new ServiceCollection().BuildServiceProvider()).DispatchAsync(new SuggestionVoted(1));
}

public class ListServiceTests
{
    private readonly IListRepository _repo = Substitute.For<IListRepository>();
    private readonly ListService _service;

    public ListServiceTests()
    {
        _repo.GetGameIdAsync("game", Arg.Any<CancellationToken>()).Returns(3);
        _service = new ListService(_repo);
    }

    [Fact]
    public async Task Adding_a_game_not_yet_listed_adds_it()
    {
        _repo.ExistsAsync("u", 3, ListKind.Wishlist, Arg.Any<CancellationToken>()).Returns(false);
        Assert.True((await _service.SetAsync("u", "Game", ListKind.Wishlist, true)).Ok);
        await _repo.Received(1).AddAsync("u", 3, ListKind.Wishlist, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adding_a_game_already_listed_is_a_no_op()
    {
        _repo.ExistsAsync("u", 3, ListKind.Played, Arg.Any<CancellationToken>()).Returns(true);
        await _service.SetAsync("u", "game", ListKind.Played, true);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default, default);
    }

    [Fact]
    public async Task Removing_a_listed_game_removes_it_and_removing_an_unlisted_one_does_nothing()
    {
        _repo.ExistsAsync("u", 3, ListKind.Wishlist, Arg.Any<CancellationToken>()).Returns(true, false);

        await _service.SetAsync("u", "game", ListKind.Wishlist, false);
        await _service.SetAsync("u", "game", ListKind.Wishlist, false);

        await _repo.Received(1).RemoveAsync("u", 3, ListKind.Wishlist, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_game_fails_and_has_an_empty_state()
    {
        Assert.False((await _service.SetAsync("u", "nope", ListKind.Wishlist, true)).Ok);
        Assert.Empty(await _service.GetStateAsync("u", "nope"));
    }
}
