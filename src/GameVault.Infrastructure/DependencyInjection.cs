using GameVault.Application.Catalogue;
using GameVault.Application.Events;
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

        services.AddHybridCache();
        services.AddScoped<GameCatalog>();
        services.AddScoped<IGameCatalog>(sp => new CachedGameCatalog(sp.GetRequiredService<GameCatalog>(), sp.GetRequiredService<HybridCache>()));
        return services;
    }
}
