using GameVault.Application.Catalogue;
using GameVault.Application.Common;
using GameVault.Domain;

namespace GameVault.Application.Lists;

public interface IListRepository
{
    Task<int?> GetGameIdAsync(string slug, CancellationToken ct = default);
    Task<bool> ExistsAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default);
    Task AddAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default);
    Task RemoveAsync(string userId, int gameId, ListKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<GameCard>> GetCardsAsync(string userId, ListKind kind, CancellationToken ct = default);
    Task<IReadOnlySet<ListKind>> GetKindsAsync(string userId, int gameId, CancellationToken ct = default);
}

public sealed class ListService
{
    private readonly IListRepository _lists;
    public ListService(IListRepository lists) => _lists = lists;

    /// <summary>Idempotent: asking for the state a game is already in changes nothing.</summary>
    public async Task<OpResult> SetAsync(string userId, string slug, ListKind kind, bool present, CancellationToken ct = default)
    {
        if (await _lists.GetGameIdAsync(slug.ToLowerInvariant(), ct) is not { } gameId) return OpResult.Fail("That game does not exist.");

        var exists = await _lists.ExistsAsync(userId, gameId, kind, ct);
        if (present && !exists) await _lists.AddAsync(userId, gameId, kind, ct);
        else if (!present && exists) await _lists.RemoveAsync(userId, gameId, kind, ct);
        return OpResult.Success;
    }

    public Task<IReadOnlyList<GameCard>> GetAsync(string userId, ListKind kind, CancellationToken ct = default)
        => _lists.GetCardsAsync(userId, kind, ct);

    public async Task<IReadOnlySet<ListKind>> GetStateAsync(string userId, string slug, CancellationToken ct = default)
        => await _lists.GetGameIdAsync(slug.ToLowerInvariant(), ct) is { } id
            ? await _lists.GetKindsAsync(userId, id, ct)
            : new HashSet<ListKind>();
}
