using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Application.Events;

public interface IDomainEvent { }

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent;
}

public sealed record ReviewChanged(int GameId) : IDomainEvent;
public sealed record SuggestionVoted(int SuggestionId) : IDomainEvent;

/// <summary>Observer: publishers raise events without knowing which handlers react.</summary>
public sealed class InProcessDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _services;
    public InProcessDispatcher(IServiceProvider services) => _services = services;

    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent
    {
        foreach (var handler in _services.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, ct);
    }
}
