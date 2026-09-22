using Amazon.Runtime;
using Ananke.Orchestration.Bedrock;
using Shouldly;

namespace Ananke.Orchestration.Bedrock.Tests;

[TestFixture]
public sealed class BedrockEndpointTests
{
    [Test]
    public void Runtime_builds_the_regional_runtime_host()
    {
        var endpoint = BedrockEndpoint.Runtime("us-west-2");

        endpoint.BaseUri.ShouldBe(new Uri("https://bedrock-runtime.us-west-2.amazonaws.com"));
        endpoint.Region.ShouldBe("us-west-2");
        endpoint.OpenAiCompatibleBaseUrl.ShouldBe(
            new Uri("https://bedrock-runtime.us-west-2.amazonaws.com/openai/v1/"));
    }

    [Test]
    public void Mantle_builds_the_regional_mantle_host()
    {
        // Different suffix from the runtime endpoint — .api.aws, not .amazonaws.com.
        BedrockEndpoint.Mantle("eu-central-1").BaseUri
            .ShouldBe(new Uri("https://bedrock-mantle.eu-central-1.api.aws"));
    }

    [Test]
    public void Custom_supports_a_privatelink_host_with_an_explicit_region()
    {
        // The whole point: a VPC interface endpoint's hostname carries no region, but SigV4 still
        // needs one for the credential scope.
        var endpoint = BedrockEndpoint.Custom(
            new Uri("https://vpce-0abc-1def.bedrock-runtime.us-east-1.vpce.amazonaws.com"),
            "us-east-1");

        endpoint.Region.ShouldBe("us-east-1");
        endpoint.OpenAiCompatibleBaseUrl.AbsoluteUri.ShouldEndWith("/openai/v1/");
    }

    [Test]
    public void Custom_rejects_a_relative_uri() =>
        Should.Throw<ArgumentException>(() =>
            BedrockEndpoint.Custom(new Uri("/openai/v1", UriKind.Relative), "us-east-1"));

    [Test]
    public void Endpoints_do_not_double_up_separators()
    {
        var endpoint = BedrockEndpoint.Custom(new Uri("https://host.example/"), "us-east-1");

        endpoint.OpenAiCompatibleBaseUrl.ShouldBe(new Uri("https://host.example/openai/v1/"));
        endpoint.AnthropicMessagesBaseUrl.ShouldBe(new Uri("https://host.example/anthropic/v1/"));
    }

    [TestCase("")]
    [TestCase("  ")]
    public void Region_is_required(string region)
    {
        Should.Throw<ArgumentException>(() => BedrockEndpoint.Runtime(region));
        Should.Throw<ArgumentException>(() => BedrockEndpoint.Mantle(region));
    }

    [Test]
    public void Signing_service_is_bedrock_for_every_endpoint_kind()
    {
        BedrockEndpoint.Runtime("us-east-1").SigningService.ShouldBe("bedrock");
        BedrockEndpoint.Mantle("us-east-1").SigningService.ShouldBe("bedrock");
        BedrockEndpoint.Custom(new Uri("https://h.example"), "us-east-1").SigningService.ShouldBe("bedrock");
    }

    // ── factories ────────────────────────────────────────────────────

    [Test]
    public void Factories_return_the_existing_adapter_types()
    {
        // The point of the package: no Bedrock-specific model type exists, because Bedrock speaks
        // two wire formats Ananke already implements.
        var endpoint = BedrockEndpoint.Runtime("us-east-1");

        BedrockAgentModel.CreateChatCompletions(endpoint, "openai.gpt-oss-20b-1:0", "key")
            .ShouldBeOfType<Orchestration.OpenAI.OpenAIChatAgentModel>();
        BedrockAgentModel.CreateMessages(endpoint, "anthropic.claude-sonnet-5-v1:0", "key")
            .ShouldBeOfType<Orchestration.Anthropic.AnthropicAgentModel>();
    }

    [Test]
    public void Factories_accept_explicit_credentials_without_touching_the_ambient_chain()
    {
        // Passing credentials must not require AWS configuration to be present on the machine.
        var endpoint = BedrockEndpoint.Runtime("us-east-1");
        var credentials = new BasicAWSCredentials("AKID", "SECRET");

        BedrockAgentModel.CreateChatCompletions(endpoint, "m", credentials).ShouldNotBeNull();
        BedrockAgentModel.CreateMessages(endpoint, "m", credentials).ShouldNotBeNull();
    }

    [Test]
    public void Factories_reject_missing_arguments()
    {
        var endpoint = BedrockEndpoint.Runtime("us-east-1");

        Should.Throw<ArgumentNullException>(() =>
            BedrockAgentModel.CreateChatCompletions(null!, "m", "key"));
        Should.Throw<ArgumentException>(() =>
            BedrockAgentModel.CreateChatCompletions(endpoint, " ", "key"));
        Should.Throw<ArgumentException>(() =>
            BedrockAgentModel.CreateMessages(endpoint, "m", apiKey: " "));
    }
}
