using GameVault.Application.Common;
using GameVault.Application.Events;
using GameVault.Domain;
using GameVault.Domain.Entities;

namespace GameVault.Application.Suggestions;

public sealed record SuggestionGame(int Id, string Slug, string Title, bool IsStudioGame);

public sealed record SuggestionItem(
    int Id, string Title, string Body, SuggestionStatus Status, int VoteCount, string AuthorName, DateTime CreatedAt, bool VotedByMe);

public interface ISuggestionRepository
{
    Task<SuggestionGame?> GetGameAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<SuggestionItem>> ListAsync(int gameId, string? currentUserId, SuggestionStatus? status, CancellationToken ct = default);
    Task<Suggestion?> GetAsync(int id, CancellationToken ct = default);
    Task<int> AddAsync(Suggestion suggestion, CancellationToken ct = default);

    /// <summary>Adds or removes the vote row and updates VoteCount in one transaction. False when nothing changed.</summary>
    Task<bool> SetVoteAsync(int suggestionId, string userId, bool present, CancellationToken ct = default);

    Task<bool> SetStatusAsync(int id, SuggestionStatus status, CancellationToken ct = default);
}

public sealed class SuggestionService
{
    public const int MinTitle = 3;
    public const int MaxTitle = 200;
    public const int MaxBody = 4000;

    private readonly ISuggestionRepository _repo;
    private readonly IDomainEventDispatcher _events;
    private readonly TimeProvider _time;

    public SuggestionService(ISuggestionRepository repo, IDomainEventDispatcher events, TimeProvider time)
    {
        _repo = repo;
        _events = events;
        _time = time;
    }

    public Task<SuggestionGame?> GetGameAsync(string slug, CancellationToken ct = default) => _repo.GetGameAsync(slug.ToLowerInvariant(), ct);

    public Task<IReadOnlyList<SuggestionItem>> ListAsync(int gameId, string? userId, SuggestionStatus? status, CancellationToken ct = default)
        => _repo.ListAsync(gameId, userId, status, ct);

    public async Task<OpResult> SubmitAsync(string userId, string slug, string? title, string? body, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        body = (body ?? "").Trim();
        if (title.Length < MinTitle || title.Length > MaxTitle) return OpResult.Fail($"The title needs {MinTitle} to {MaxTitle} characters.");
        if (body.Length > MaxBody) return OpResult.Fail($"The details can be at most {MaxBody} characters.");

        var game = await _repo.GetGameAsync(slug.ToLowerInvariant(), ct);
        if (game is null) return OpResult.Fail("That game does not exist.");
        if (!game.IsStudioGame) return OpResult.Fail("Suggestions are only open for the studio's own games.");

        await _repo.AddAsync(new Suggestion
        {
            GameId = game.Id, AuthorId = userId, Title = title, Body = body, CreatedAt = _time.GetUtcNow().UtcDateTime
        }, ct);
        return OpResult.Success;
    }

    public async Task<OpResult> VoteAsync(string userId, int suggestionId, bool present, CancellationToken ct = default)
    {
        if (await _repo.GetAsync(suggestionId, ct) is null) return OpResult.Fail("That suggestion does not exist.");
        if (await _repo.SetVoteAsync(suggestionId, userId, present, ct))
            await _events.DispatchAsync(new SuggestionVoted(suggestionId), ct);
        return OpResult.Success;
    }

    public async Task<OpResult> SetStatusAsync(int suggestionId, SuggestionStatus status, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status)) return OpResult.Fail("Unknown status.");
        return await _repo.SetStatusAsync(suggestionId, status, ct) ? OpResult.Success : OpResult.Fail("That suggestion does not exist.");
    }
}
