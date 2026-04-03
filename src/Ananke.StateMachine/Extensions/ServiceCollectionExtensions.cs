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
}
