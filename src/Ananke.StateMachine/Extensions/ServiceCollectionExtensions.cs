using Ananke.Abstractions;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ananke.StateMachine.Extensions;

/// <summary>
/// DI registration extensions for Ananke.StateMachine.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers state machine infrastructure with default options.
    /// Registers <see cref="InMemoryDistributedLock"/> as a fallback <see cref="IDistributedLock"/>
    /// and default <see cref="StateMachineOptions"/>. Infrastructure packages (e.g. Ananke.Redis)
    /// replace the in-memory fallback when added.
    /// </summary>
    public static IServiceCollection AddStateMachine(this IServiceCollection services)
    {
        return services.AddStateMachine(_ => { });
    }

    /// <summary>
    /// Registers state machine infrastructure with the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback to configure <see cref="StateMachineServiceOptions"/>.</param>
    public static IServiceCollection AddStateMachine(
        this IServiceCollection services,
        Action<StateMachineServiceOptions> configure)
    {
        var options = new StateMachineServiceOptions();
        configure(options);

        services.TryAddSingleton(options.StateMachineOptions);
        services.TryAddSingleton<InMemoryDistributedLock>();
        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<InMemoryDistributedLock>());
        services.TryAddSingleton<IKeyValueDataAdapter>(sp => sp.GetRequiredService<InMemoryDistributedLock>());

        return services;
    }

    /// <summary>
    /// Registers a concrete state machine as both its implementation type and
    /// <see cref="IActionStateMachine{C, S, T, N}"/>.
    /// </summary>
    /// <typeparam name="TImpl">Concrete state machine type.</typeparam>
    /// <typeparam name="TContext">Context type implementing <see cref="Abstractions.IBaseContext"/>.</typeparam>
    /// <typeparam name="TState">State enum type.</typeparam>
    /// <typeparam name="TTransition">Transition enum type.</typeparam>
    /// <typeparam name="TNotification">Notification enum type.</typeparam>
    public static IServiceCollection AddStateMachine<TImpl, TContext, TState, TTransition, TNotification>(
        this IServiceCollection services)
        where TImpl : class, IActionStateMachine<TContext, TState, TTransition, TNotification>
        where TContext : Abstractions.IBaseContext
        where TState : Enum
        where TTransition : Enum
        where TNotification : Enum
    {
        services.TryAddSingleton<TImpl>();
        services.TryAddSingleton<IActionStateMachine<TContext, TState, TTransition, TNotification>>(
            sp => sp.GetRequiredService<TImpl>());

        return services;
    }

    /// <summary>
    /// Registers a concrete state machine with its lock, state store and the channels for its transition and
    /// notification enums, in one call. <paramref name="configure"/> chooses the infrastructure
    /// (<c>UseRedis</c> from Ananke.Redis, <c>UseMqtt</c> from Ananke.MQTT); without them the lock and state store
    /// are <see cref="InMemoryDistributedLock"/>, and each enum with neither a reader nor a writer registered gets an
    /// <see cref="InMemoryChannelReader{M, A}"/> with an <see cref="InMemoryChannelWriter{A}"/> linked to it; an enum
    /// with only one of them registered gets nothing, so resolving the other fails instead of reading from a channel
    /// no writer feeds. Registrations the host made before this call are kept, except those <c>UseRedis</c> and
    /// <c>UseMqtt</c> replace.
    /// </summary>
    /// <typeparam name="TImpl">Concrete state machine type.</typeparam>
    /// <typeparam name="TContext">Context type; also the message type of both channels.</typeparam>
    /// <typeparam name="TState">State enum type.</typeparam>
    /// <typeparam name="TTransition">Transition enum type.</typeparam>
    /// <typeparam name="TNotification">Notification enum type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the infrastructure on the builder.</param>
    public static IServiceCollection AddStateMachine<TImpl, TContext, TState, TTransition, TNotification>(
        this IServiceCollection services,
        Action<StateMachineBuilder<IServiceCollection, TContext, TTransition, TNotification>> configure)
        where TImpl : class, IActionStateMachine<TContext, TState, TTransition, TNotification>
        where TContext : class, Abstractions.IBaseContext
        where TState : Enum
        where TTransition : Enum
        where TNotification : Enum
    {
        configure(new StateMachineBuilder<IServiceCollection, TContext, TTransition, TNotification>(services));

        services.AddStateMachine();
        TryAddInMemoryChannel<TContext, TTransition>(services);
        TryAddInMemoryChannel<TContext, TNotification>(services);

        return services.AddStateMachine<TImpl, TContext, TState, TTransition, TNotification>();
    }

    private static void TryAddInMemoryChannel<TContext, TAction>(IServiceCollection services)
        where TContext : class
        where TAction : Enum
    {
        if (services.Any(d => !d.IsKeyedService
                && (d.ServiceType == typeof(IChannelReader<TContext, TAction>) || d.ServiceType == typeof(IChannelWriter<TAction>))))
            return;

        services.TryAddSingleton<InMemoryChannelReader<TContext, TAction>>();
        services.TryAddSingleton<IChannelReader<TContext, TAction>>(
            sp => sp.GetRequiredService<InMemoryChannelReader<TContext, TAction>>());
        services.TryAddSingleton<IChannelWriter<TAction>>(
            sp => new InMemoryChannelWriter<TAction>().LinkTo(sp.GetRequiredService<InMemoryChannelReader<TContext, TAction>>()));
    }
}
