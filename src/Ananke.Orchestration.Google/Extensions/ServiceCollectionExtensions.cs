using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Google.Credentials;
using Ananke.Orchestration.Google.Translators;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.Orchestration.Google.Extensions;

/// <summary>
/// DI registration helpers for the Google Gemini orchestration provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gemini Developer API provider services using an API key.
    /// Registers <see cref="IToolSchemaTranslator"/>, <see cref="ISystemPromptCompiler"/>,
    /// <see cref="IModelMapper"/>, <see cref="IJsonSchemaTranslator"/>, and
    /// <see cref="ICredentialProvider"/> implementations.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="apiKey">
    /// Optional Gemini API key. When <see langword="null"/>, <c>GEMINI_API_KEY</c> then
    /// <c>GOOGLE_API_KEY</c> environment variables are consulted.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOrchestrationGoogle(
        this IServiceCollection services,
        string? apiKey = null)
    {
        services.AddSingleton<IToolSchemaTranslator, GeminiToolSchemaTranslator>();
        services.AddSingleton<ISystemPromptCompiler, GeminiSystemPromptCompiler>();
        services.AddSingleton<IModelMapper, GeminiModelMapper>();
        services.AddSingleton<IJsonSchemaTranslator, GeminiJsonSchemaTranslator>();
        services.AddSingleton<ICredentialProvider>(new GeminiApiKeyCredentialProvider(apiKey));
        return services;
    }

    /// <summary>
    /// Registers Vertex AI provider services using Application Default Credentials (ADC).
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="project">Google Cloud project ID.</param>
    /// <param name="location">Google Cloud region (e.g. <c>"us-central1"</c>).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOrchestrationVertexAI(
        this IServiceCollection services,
        string project,
        string location)
    {
        services.AddSingleton<IToolSchemaTranslator, GeminiToolSchemaTranslator>();
        services.AddSingleton<ISystemPromptCompiler, GeminiSystemPromptCompiler>();
        services.AddSingleton<IModelMapper, GeminiModelMapper>();
        services.AddSingleton<IJsonSchemaTranslator, GeminiJsonSchemaTranslator>();
        services.AddSingleton<ICredentialProvider>(new VertexAICredentialProvider(project, location));
        return services;
    }
}
