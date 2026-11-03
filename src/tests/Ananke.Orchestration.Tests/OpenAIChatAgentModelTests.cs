using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.TestHelpers.ProviderStubs;
using OpenAI;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class OpenAIChatAgentModelTests
{
    [Test]
    public async Task GenerateAsync_ToolCallsThatFinishWithStop_RequireAction()
    {
        // The streaming path takes any call it assembled; the other path used to require the
        // finish_reason as well, so the same reply meant a tool call on one path and text on the other.
        var model = Model(new OpenAIStubHandler { ToolCallFinish = "stop" });

        var response = await model.GenerateAsync(new AgentRequest
        {
            Messages = [AgentMessage.User("look it up")],
            Tools = [new AgentTool("look_up", "Looks something up.", """{"type":"object","properties":{}}""")]
        });

        response.RequiresAction.ShouldBeTrue();
        response.ToolCalls.ShouldNotBeNull().ShouldHaveSingleItem().FunctionName.ShouldBe("look_up");
        response.StopReason.ShouldBe(AgentStopReason.ToolCalls);
    }

    [TestCase(typeof(AuthenticationPolicy))]
    [TestCase(typeof(AuthenticationTokenProvider))]
    public void Create_OverTheSdksExperimentalConstructors_IsMarkedExperimental(Type credential)
    {
        // Both reach ChatClient constructors the OpenAI SDK marks OPENAI001, so a consumer is told
        // in the compiler, under Ananke's own id, that the surface may move with the SDK.
        var create = typeof(OpenAIChatAgentModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(OpenAIChatAgentModel.Create)
                         && m.GetParameters()[0].ParameterType == credential);

        create.GetCustomAttribute<ExperimentalAttribute>().ShouldNotBeNull().DiagnosticId.ShouldBe("ANNKEEXP002");
    }

    private static OpenAIChatAgentModel Model(HttpMessageHandler handler) =>
        new(new ChatClient("gpt-4.1-mini", new ApiKeyCredential("stub-key"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        }));
}
