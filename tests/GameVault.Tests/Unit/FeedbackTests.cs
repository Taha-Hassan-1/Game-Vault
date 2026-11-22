using GameVault.Application.Catalogue;
using GameVault.Application.Events;
using GameVault.Application.Feedback;
using GameVault.Application.Studio;
using GameVault.Application.Suggestions;
using GameVault.Domain;
using GameVault.Domain.Entities;
using NSubstitute;

namespace GameVault.Tests.Unit;

public class ApiKeyServiceTests
{
    private readonly IApiKeyRepository _keys = Substitute.For<IApiKeyRepository>();
    private readonly IStudioGameRepository _games = Substitute.For<IStudioGameRepository>();
    private readonly ApiKeyService _service;

    public ApiKeyServiceTests()
    {
        _games.IsStudioGameAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _service = new ApiKeyService(_keys, _games, TimeProvider.System);
    }

    [Fact]
    public void Generated_keys_are_prefixed_long_url_safe_and_unique()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => ApiKeyService.Generate()).ToList();

        Assert.Equal(50, keys.Distinct().Count());
        Assert.All(keys, k =>
        {
            Assert.StartsWith("gv_", k);
            Assert.Equal(3 + 43, k.Length); // 32 bytes as unpadded base64
            Assert.Matches("^[A-Za-z0-9_-]+$", k[3..]);
        });
    }

    [Fact]
    public async Task Creating_a_key_stores_only_the_hash_and_a_short_prefix()
    {
        ApiKey? saved = null;
        await _keys.AddAsync(Arg.Do<ApiKey>(k => saved = k), Arg.Any<CancellationToken>());

        var r = await _service.CreateAsync(1);

        Assert.True(r.Ok);
        Assert.NotNull(saved);
        Assert.Equal(KeyHasher.Hash(r.Value!.Plaintext), saved!.KeyHash);
        Assert.NotEqual(r.Value.Plaintext, saved.KeyHash);
        Assert.Equal(r.Value.Plaintext[..10], saved.KeyPrefix);
        Assert.DoesNotContain(r.Value.Plaintext, saved.KeyHash + saved.KeyPrefix);
        Assert.Equal(1, saved.GameId);
    }

    [Fact]
    public async Task Keys_are_only_created_for_studio_games()
    {
        Assert.False((await _service.CreateAsync(2)).Ok);
        await _keys.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task A_valid_key_resolves_to_its_game_through_its_hash()
    {
        var key = ApiKeyService.Generate();
        _keys.FindActiveGameIdAsync(KeyHasher.Hash(key), Arg.Any<CancellationToken>()).Returns(7);

        Assert.Equal(7, await _service.AuthenticateAsync(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("xx_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Malformed_keys_are_rejected_without_a_database_lookup(string? key)
    {
        Assert.Null(await _service.AuthenticateAsync(key));
        await _keys.DidNotReceiveWithAnyArgs().FindActiveGameIdAsync(default!);
    }

    [Fact]
    public async Task An_unknown_or_revoked_key_resolves_to_nothing()
    {
        _keys.FindActiveGameIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((int?)null);
        Assert.Null(await _service.AuthenticateAsync(ApiKeyService.Generate()));
    }

    [Fact]
    public void Hashing_is_stable_and_hex()
    {
        Assert.Equal(KeyHasher.Hash("gv_x"), KeyHasher.Hash("gv_x"));
        Assert.NotEqual(KeyHasher.Hash("gv_x"), KeyHasher.Hash("gv_y"));
        Assert.Matches("^[0-9a-f]{64}$", KeyHasher.Hash("gv_x"));
    }
}

public class FeedbackServiceTests
{
    private readonly IFeedbackRepository _repo = Substitute.For<IFeedbackRepository>();
    private readonly IStudioGameRepository _games = Substitute.For<IStudioGameRepository>();
    private readonly FeedbackService _service;

    public FeedbackServiceTests()
    {
        _games.IsStudioGameAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _repo.AddAsync(Arg.Any<FeedbackMessage>(), Arg.Any<CancellationToken>()).Returns(42);
        _service = new FeedbackService(_repo, _games, TimeProvider.System);
    }

    private Task<FeedbackResult> Submit(string? category = "Bug", string? body = "It broke", string? player = null, string? version = null, int game = 1)
        => _service.SubmitAsync(new FeedbackSubmission(game, FeedbackSource.Api, category, body, player, version));

    [Fact]
    public async Task A_valid_message_is_stored_trimmed_with_its_source()
    {
        FeedbackMessage? saved = null;
        await _repo.AddAsync(Arg.Do<FeedbackMessage>(m => saved = m), Arg.Any<CancellationToken>());

        var r = await Submit("bug", "  It broke  ", " anon-1 ", " 1.4.0 ");

        Assert.True(r.Ok);
        Assert.Equal(42, r.Id);
        Assert.Equal(FeedbackCategory.Bug, saved!.Category);
        Assert.Equal("It broke", saved.Body);
        Assert.Equal("anon-1", saved.PlayerRef);
        Assert.Equal("1.4.0", saved.AppVersion);
        Assert.Equal(FeedbackSource.Api, saved.Source);
        Assert.Equal(FeedbackStatus.New, saved.Status);
    }

    [Theory]
    [InlineData("Bug", FeedbackCategory.Bug)]
    [InlineData("IDEA", FeedbackCategory.Idea)]
    [InlineData(" praise ", FeedbackCategory.Praise)]
    public void Categories_are_matched_by_name_ignoring_case(string text, FeedbackCategory expected)
        => Assert.Equal(expected, FeedbackService.ParseCategory(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("Bug, Idea")]
    [InlineData("Complaint")]
    public async Task Anything_that_is_not_a_category_name_is_refused(string? category)
    {
        var r = await Submit(category);

        Assert.False(r.Ok);
        Assert.Contains("category", r.Errors.Keys);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task The_body_is_required_and_capped()
    {
        Assert.Contains("body", (await Submit(body: "   ")).Errors.Keys);
        Assert.Contains("body", (await Submit(body: new string('a', FeedbackService.MaxBody + 1))).Errors.Keys);
        Assert.True((await Submit(body: new string('a', FeedbackService.MaxBody))).Ok);
    }

    [Theory]
    [InlineData("1.4.0", true)]
    [InlineData("2024.3-beta+7", true)]
    [InlineData("v1", true)]
    [InlineData("1.4.0; DROP TABLE", false)]
    [InlineData("<b>1</b>", false)]
    [InlineData("-1", false)]
    [InlineData("12345678901234567890123456789012345", false)]
    public async Task The_app_version_must_look_like_a_version(string version, bool ok)
        => Assert.Equal(ok, (await Submit(version: version)).Ok);

    [Fact]
    public async Task The_player_reference_cannot_carry_control_characters_or_run_on()
    {
        Assert.False((await Submit(player: "a\r\nb")).Ok);
        Assert.False((await Submit(player: new string('x', 101))).Ok);
        Assert.True((await Submit(player: new string('x', 100))).Ok);
    }

    [Fact]
    public async Task All_problems_are_reported_together()
    {
        var r = await Submit(category: "x", body: "", version: "!!");
        Assert.Equal(new[] { "appVersion", "body", "category" }, r.Errors.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Feedback_for_a_game_that_is_not_a_studio_game_is_refused()
    {
        var r = await Submit(game: 99);
        Assert.False(r.Ok);
        Assert.Contains("game", r.Errors.Keys);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("", "")]
    public void Csv_cells_are_quoted_and_cannot_run_as_formulas(string value, string expected)
        => Assert.Equal(expected, Csv.Cell(value));

    [Fact]
    public async Task The_export_has_a_header_one_row_per_message_and_crlf_endings()
    {
        _repo.ExportAsync(Arg.Any<FeedbackFilter>(), FeedbackService.ExportLimit, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new FeedbackItem(1, 1, "Untangle", FeedbackSource.Api, FeedbackCategory.Bug, "=cmd|' /C calc'!A0", "anon", "1.0", FeedbackStatus.New,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))
        });

        var csv = await _service.ExportCsvAsync(new FeedbackFilter());

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id,Game,Source,Category,Status,AppVersion,PlayerRef,CreatedUtc,Body", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1,Untangle,Api,Bug,New,1.0,anon,2026-01-02 03:04:05,'=cmd", lines[1]);
    }

    [Fact]
    public void Filters_are_normalised()
    {
        var f = new FeedbackFilter(Page: -4, PageSize: 9999).Normalized();
        Assert.Equal(1, f.Page);
        Assert.Equal(FeedbackFilter.MaxPageSize, f.PageSize);
    }

    [Fact]
    public async Task Setting_an_undefined_status_is_refused_without_touching_the_database()
    {
        Assert.False(await _service.SetStatusAsync(1, (FeedbackStatus)99));
        await _repo.DidNotReceiveWithAnyArgs().SetStatusAsync(default, default);
    }
}

public class SuggestionServiceTests
{
    private readonly ISuggestionRepository _repo = Substitute.For<ISuggestionRepository>();
    private readonly IDomainEventDispatcher _events = Substitute.For<IDomainEventDispatcher>();
    private readonly SuggestionService _service;

    public SuggestionServiceTests()
    {
        _repo.GetGameAsync("untangle", Arg.Any<CancellationToken>()).Returns(new SuggestionGame(1, "untangle", "Untangle", true));
        _repo.GetGameAsync("hades", Arg.Any<CancellationToken>()).Returns(new SuggestionGame(2, "hades", "Hades", false));
        _service = new SuggestionService(_repo, _events, TimeProvider.System);
    }

    [Fact]
    public async Task A_suggestion_is_stored_trimmed_as_new()
    {
        Suggestion? saved = null;
        await _repo.AddAsync(Arg.Do<Suggestion>(s => saved = s), Arg.Any<CancellationToken>());

        var r = await _service.SubmitAsync("u1", "Untangle", "  Add a dark mode  ", "  please ");

        Assert.True(r.Ok);
        Assert.Equal("Add a dark mode", saved!.Title);
        Assert.Equal("please", saved.Body);
        Assert.Equal(SuggestionStatus.New, saved.Status);
        Assert.Equal(1, saved.GameId);
        Assert.Equal("u1", saved.AuthorId);
    }

    [Theory]
    [InlineData("ab", "x")]
    [InlineData("   ", "x")]
    public async Task A_short_title_is_refused(string title, string body)
        => Assert.False((await _service.SubmitAsync("u", "untangle", title, body)).Ok);

    [Fact]
    public async Task Over_long_text_is_refused()
    {
        Assert.False((await _service.SubmitAsync("u", "untangle", new string('a', 201), "")).Ok);
        Assert.False((await _service.SubmitAsync("u", "untangle", "Fine title", new string('a', 4001))).Ok);
    }

    [Fact]
    public async Task Only_studio_games_have_a_board()
    {
        Assert.False((await _service.SubmitAsync("u", "hades", "A fine idea", "")).Ok);
        Assert.False((await _service.SubmitAsync("u", "nope", "A fine idea", "")).Ok);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Voting_announces_only_a_real_change()
    {
        _repo.GetAsync(5, Arg.Any<CancellationToken>()).Returns(new Suggestion { Id = 5 });
        _repo.SetVoteAsync(5, "u", true, Arg.Any<CancellationToken>()).Returns(true, false);

        await _service.VoteAsync("u", 5, true);
        await _service.VoteAsync("u", 5, true); // repeat: no change

        await _events.Received(1).DispatchAsync(Arg.Is<SuggestionVoted>(e => e.SuggestionId == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Voting_on_a_missing_suggestion_fails()
    {
        _repo.GetAsync(9, Arg.Any<CancellationToken>()).Returns((Suggestion?)null);
        Assert.False((await _service.VoteAsync("u", 9, true)).Ok);
        await _repo.DidNotReceiveWithAnyArgs().SetVoteAsync(default, default!, default);
    }

    [Fact]
    public async Task Status_changes_validate_the_value_and_the_target()
    {
        _repo.SetStatusAsync(5, SuggestionStatus.Shipped, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SetStatusAsync(6, Arg.Any<SuggestionStatus>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.True((await _service.SetStatusAsync(5, SuggestionStatus.Shipped)).Ok);
        Assert.False((await _service.SetStatusAsync(6, SuggestionStatus.Planned)).Ok);
        Assert.False((await _service.SetStatusAsync(5, (SuggestionStatus)42)).Ok);
    }
}
