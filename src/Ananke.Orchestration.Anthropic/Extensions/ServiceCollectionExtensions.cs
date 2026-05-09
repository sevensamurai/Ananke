using Ananke.Orchestration.Anthropic.Credentials;
using Ananke.Orchestration.Anthropic.Translators;
using Ananke.Orchestration.Credentials;
using Ananke.Orchestration.Translators;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.Orchestration.Anthropic.Extensions;

/// <summary>
/// DI registration helpers for the Anthropic orchestration provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Anthropic provider services: <see cref="IToolSchemaTranslator"/>,
    /// <see cref="ISystemPromptCompiler"/>, <see cref="IModelMapper"/>, and
    /// <see cref="ICredentialProvider"/> implementations.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="apiKey">
    /// Optional Anthropic API key. When <see langword="null"/>, the
    /// <c>ANTHROPIC_API_KEY</c> environment variable is used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOrchestrationAnthropic(
        this IServiceCollection services,
        string? apiKey = null)
    {
        services.AddSingleton<IToolSchemaTranslator, AnthropicToolSchemaTranslator>();
        services.AddSingleton<ISystemPromptCompiler, AnthropicSystemPromptCompiler>();
        services.AddSingleton<IModelMapper, AnthropicModelMapper>();
        services.AddSingleton<ICredentialProvider>(new AnthropicCredentialProvider(apiKey));
        return services;
    }
}
