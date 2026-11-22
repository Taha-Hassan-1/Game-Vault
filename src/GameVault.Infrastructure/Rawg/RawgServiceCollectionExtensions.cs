using GameVault.Application.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Infrastructure.Rawg;

public static class RawgServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RAWG client with its resilience pipeline and the request budget.
    /// The caller supplies <see cref="IApiUsageStore"/>, so tests can run without a database.
    /// </summary>
    public static IServiceCollection AddRawgIntegration(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RawgOptions>().Bind(config.GetSection(RawgOptions.Section))
            .Validate(o => Uri.TryCreate(o.BaseAddress, UriKind.Absolute, out _), "Rawg:BaseAddress must be an absolute URL.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ApiKey), "Rawg:ApiKey is required when Rawg:Enabled is true.")
            .Validate(o => o.PageSize is >= 1 and <= 40, "Rawg:PageSize must be between 1 and 40.")
            .ValidateOnStart();
        services.AddOptions<SyncBudgetOptions>().Bind(config.GetSection(SyncBudgetOptions.Section))
            .Validate(o => o.SyncLimit <= o.MonthlyLimit, "SyncBudget:SyncLimit cannot exceed MonthlyLimit.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRequestBudget, RequestBudget>();
        services.AddTransient<BudgetHandler>();

        // Resilience settings are read now because the pipeline is built once, at registration.
        var o = config.GetSection(RawgOptions.Section).Get<RawgOptions>() ?? new RawgOptions();
        var attempt = TimeSpan.FromSeconds(o.AttemptTimeoutSeconds);

        var client = services.AddHttpClient<RawgGameProvider>(c =>
        {
            c.BaseAddress = new Uri(o.BaseAddress.TrimEnd('/') + "/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("GameVault/1.0");
        });

        // Handlers run outer to inner in the order added: resilience first, so the budget handler counts every retry attempt.
        client.AddStandardResilienceHandler(r =>
        {
            r.Retry.MaxRetryAttempts = 3;
            r.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            r.Retry.UseJitter = true;
            r.Retry.Delay = TimeSpan.FromMilliseconds(o.RetryBaseDelayMs);
            r.Retry.ShouldRetryAfterHeader = true;
            r.AttemptTimeout.Timeout = attempt;
            r.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(o.TotalTimeoutSeconds, o.AttemptTimeoutSeconds));
            r.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, o.AttemptTimeoutSeconds * 2));
            r.CircuitBreaker.MinimumThroughput = 10;
        });
        client.AddHttpMessageHandler<BudgetHandler>();

        services.AddTransient<IGameMetadataProvider>(sp => sp.GetRequiredService<RawgGameProvider>());
        services.AddTransient<IGameMetadataProviderFactory, GameMetadataProviderFactory>();
        return services;
    }
}
