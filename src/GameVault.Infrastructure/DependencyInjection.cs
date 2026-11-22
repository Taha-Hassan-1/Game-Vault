using GameVault.Application.Catalogue;
using GameVault.Application.Events;
using GameVault.Application.Feedback;
using GameVault.Application.Lists;
using GameVault.Application.Studio;
using GameVault.Application.Suggestions;
using GameVault.Infrastructure.Storage;
using GameVault.Application.Metadata;
using GameVault.Application.Reviews;
using GameVault.Infrastructure.Rawg;
using GameVault.Infrastructure.Caching;
using GameVault.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGameVault(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("GameVault")
                 ?? throw new InvalidOperationException("Connection string 'GameVault' is missing.");
        services.AddDbContext<GameVaultDbContext>(o => o.UseSqlServer(cs));

        services.AddScoped<IGameRepository, EfGameRepository>();
        services.AddSingleton<ISimilarityStrategy, TagOverlapSimilarity>();
        services.AddScoped<IDomainEventDispatcher, InProcessDispatcher>();

        services.AddSingleton<IApiUsageStore, EfApiUsageStore>();
        services.AddScoped<IExternalGameStore, EfExternalGameStore>();
        services.AddRawgIntegration(config);
        services.AddScoped<GameImportService>();
        services.AddHostedService<RawgSyncWorker>();

        services.AddScoped<IReviewRepository, EfReviewRepository>();
        services.AddScoped<IListRepository, EfListRepository>();
        services.AddScoped<ReviewService>();
        services.AddScoped<ModerationService>();
        services.AddScoped<ListService>();
        services.AddScoped<IDomainEventHandler<ReviewChanged>, RatingRecalculator>();

        services.AddOptions<UploadsOptions>().Bind(config.GetSection(UploadsOptions.Section));
        services.AddSingleton<IImageStore, FileImageStore>();
        services.AddScoped<IStudioGameRepository, EfStudioGameRepository>();
        services.AddScoped<IFeedbackRepository, EfFeedbackRepository>();
        services.AddScoped<IApiKeyRepository, EfApiKeyRepository>();
        services.AddScoped<ISuggestionRepository, EfSuggestionRepository>();
        services.AddScoped<StudioGameService>();
        services.AddScoped<FeedbackService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<SuggestionService>();

        services.AddHybridCache();
        services.AddScoped<GameCatalog>();

        // The decorator is the only thing that adds caching, so this one registration is the whole switch. It exists so the
        // difference can be measured (docs/PERFORMANCE.md); leave it on in production.
        if (config.GetValue("Catalogue:CacheEnabled", true))
            services.AddScoped<IGameCatalog>(sp => new CachedGameCatalog(sp.GetRequiredService<GameCatalog>(), sp.GetRequiredService<HybridCache>()));
        else
            services.AddScoped<IGameCatalog>(sp => sp.GetRequiredService<GameCatalog>());
        return services;
    }
}
