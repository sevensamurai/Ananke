using Ananke.Orchestration.Credentials;
using Ananke.Orchestration.OpenAI.Credentials;
using Ananke.Orchestration.OpenAI.Translators;
using Ananke.Orchestration.Translators;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.Orchestration.OpenAI.Extensions;

/// <summary>
/// DI registration helpers for the OpenAI orchestration provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenAI provider services: <see cref="IToolSchemaTranslator"/>,
    /// <see cref="ISystemPromptCompiler"/>, <see cref="IModelMapper"/>, and
    /// <see cref="ICredentialProvider"/> implementations.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="apiKey">
    /// Optional OpenAI API key. When <see langword="null"/>, the
    /// <c>OPENAI_API_KEY</c> environment variable is used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOrchestrationOpenAI(
        this IServiceCollection services,
        string? apiKey = null)
    {
        services.AddSingleton<IToolSchemaTranslator, OpenAIToolSchemaTranslator>();
        services.AddSingleton<ISystemPromptCompiler, OpenAISystemPromptCompiler>();
        services.AddSingleton<IModelMapper, OpenAIModelMapper>();
        services.AddSingleton<ICredentialProvider>(new OpenAICredentialProvider(apiKey));
        return services;
    }
}
