using Microsoft.Extensions.Options;

namespace GameVault.Application.Metadata;

public sealed class RawgOptions
{
    public const string Section = "Rawg";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseAddress { get; set; } = "https://api.rawg.io/api/";
    public int PageSize { get; set; } = 40;
    public int StaleAfterDays { get; set; } = 30;
    public double AttemptTimeoutSeconds { get; set; } = 10;
    public double TotalTimeoutSeconds { get; set; } = 30;
    public int RetryBaseDelayMs { get; set; } = 1000;
}

public sealed class SyncBudgetOptions
{
    public const string Section = "SyncBudget";

    /// <summary>RAWG's free plan allows 20,000 requests a month.</summary>
    public int MonthlyLimit { get; set; } = 20_000;

    /// <summary>The nightly job stops here (75%) and leaves the rest for on-demand fetches.</summary>
    public int SyncLimit { get; set; } = 15_000;

    public int PagesPerRun { get; set; } = 5;
    public int RefreshPerRun { get; set; } = 50;
    public int InitialDelayMinutes { get; set; } = 1;

    /// <summary>Turns off only the nightly import and refresh. Lazy detail fetches on page open still work (they need Rawg:Enabled).</summary>
    public bool SyncEnabled { get; set; } = true;
}

public sealed class BudgetExceededException : Exception
{
    public BudgetExceededException(string provider) : base($"The monthly request budget for '{provider}' is used up.") { }
}

public interface IApiUsageStore
{
    Task<int> GetMonthTotalAsync(string provider, DateOnly anyDayInMonth, CancellationToken ct = default);
    Task IncrementAsync(string provider, DateOnly day, CancellationToken ct = default);
}

public interface IRequestBudget
{
    /// <summary>Counts one outbound request; false (and no count) once the monthly limit is reached.</summary>
    Task<bool> TryConsumeAsync(CancellationToken ct = default);

    /// <summary>Whether the background sync may still spend requests.</summary>
    Task<bool> CanSyncAsync(CancellationToken ct = default);
}

public sealed class RequestBudget : IRequestBudget
{
    public const string Provider = "rawg";

    private readonly IApiUsageStore _store;
    private readonly SyncBudgetOptions _options;
    private readonly TimeProvider _time;

    public RequestBudget(IApiUsageStore store, IOptions<SyncBudgetOptions> options, TimeProvider time)
    {
        _store = store;
        _options = options.Value;
        _time = time;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    public async Task<bool> TryConsumeAsync(CancellationToken ct = default)
    {
        if (await _store.GetMonthTotalAsync(Provider, Today, ct) >= _options.MonthlyLimit) return false;
        await _store.IncrementAsync(Provider, Today, ct);
        return true;
    }

    public async Task<bool> CanSyncAsync(CancellationToken ct = default)
        => await _store.GetMonthTotalAsync(Provider, Today, ct) < _options.SyncLimit;
}
