using Ananke.AspNetCore.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Ananke services into an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class AnankeServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Ananke services and returns an <see cref="IAnankeBuilder"/>
    /// for opt-in registration of additional subsystems (organic host,
    /// model providers, knowledge stores, federation).
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional delegate to configure top-level <see cref="AnankeOptions"/>.</param>
    public static IAnankeBuilder AddAnanke(
        this IServiceCollection services,
        Action<AnankeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 5.3: Register AnankeOptions through the Options infrastructure so that
        // IOptions<AnankeOptions> is resolvable from DI rather than being constructed
        // locally and discarded.
        if (configure is not null)
            services.Configure<AnankeOptions>(configure);
        else
            services.AddOptions<AnankeOptions>();

        return new AnankeBuilder(services);
    }
}
