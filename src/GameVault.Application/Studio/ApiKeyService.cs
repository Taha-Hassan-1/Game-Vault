using System.Security.Cryptography;
using System.Text;
using GameVault.Application.Common;
using GameVault.Domain.Entities;

namespace GameVault.Application.Studio;

public sealed record ApiKeyRow(int Id, int GameId, string GameTitle, string Prefix, DateTime CreatedAt, DateTime? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}

public sealed record NewApiKey(int Id, string Plaintext, string Prefix);

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey key, CancellationToken ct = default);
    Task<int?> FindActiveGameIdAsync(string keyHash, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKeyRow>> ListAsync(CancellationToken ct = default);
    Task<bool> RevokeAsync(int id, DateTime now, CancellationToken ct = default);
}

public static class KeyHasher
{
    /// <summary>SHA-256 is enough here: keys are 256 random bits, so there is nothing to brute-force or look up in a table.</summary>
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}

public sealed class ApiKeyService
{
    public const string Prefix = "gv_";
    private const int PrefixShown = 10;

    private readonly IApiKeyRepository _keys;
    private readonly IStudioGameRepository _games;
    private readonly TimeProvider _time;

    public ApiKeyService(IApiKeyRepository keys, IStudioGameRepository games, TimeProvider time)
    {
        _keys = keys;
        _games = games;
        _time = time;
    }

    public static string Generate()
    {
        var b64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Prefix + b64;
    }

    public Task<IReadOnlyList<ApiKeyRow>> ListAsync(CancellationToken ct = default) => _keys.ListAsync(ct);

    /// <summary>The plaintext is returned once, here. Only its hash and a short prefix for recognition are stored.</summary>
    public async Task<OpResult<NewApiKey>> CreateAsync(int gameId, CancellationToken ct = default)
    {
        if (!await _games.IsStudioGameAsync(gameId, ct)) return OpResult<NewApiKey>.Fail("Keys can only be created for studio games.");

        var plaintext = Generate();
        var key = new ApiKey
        {
            GameId = gameId,
            KeyPrefix = plaintext[..PrefixShown],
            KeyHash = KeyHasher.Hash(plaintext),
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };
        await _keys.AddAsync(key, ct);
        return OpResult<NewApiKey>.Success(new NewApiKey(key.Id, plaintext, key.KeyPrefix));
    }

    /// <summary>The game an active key belongs to, or null for a missing, malformed, unknown or revoked key.</summary>
    public async Task<int?> AuthenticateAsync(string? key, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key) || key.Length is < 20 or > 80 || !key.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return await _keys.FindActiveGameIdAsync(KeyHasher.Hash(key), ct);
    }

    public async Task<OpResult> RevokeAsync(int id, CancellationToken ct = default)
        => await _keys.RevokeAsync(id, _time.GetUtcNow().UtcDateTime, ct)
            ? OpResult.Success
            : OpResult.Fail("That key does not exist or is already revoked.");
}
