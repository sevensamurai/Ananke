using System.Text.Json;
using Ananke.Abstractions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Whether a value a type marks with <see cref="RedactAttribute"/> reaches any record of a run.
/// </summary>
/// <remarks>
/// <para>
/// One value, on every path a typed value is recorded through: the workflow's state, a tool's list of
/// records, a tool's typed result. Every event is written with <see cref="AnankeJson.Record"/>, and a
/// tracer and a logger are attached. A path that keeps the value is a path around the mark.
/// </para>
/// <para>
/// A tool's error that quotes the value is text, with no type to mark, and is not covered here.
/// </para>
/// </remarks>
[TestFixture]
public class RedactionReachTests
{
    private const string Phone = "600 123 456";

    private sealed record Guest
    {
        [Redact]
        public string Phone { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }

    private sealed record Booking
    {
        public string Reference { get; init; } = string.Empty;

        [Redact]
        public string Phone { get; init; } = string.Empty;
    }

    private sealed record State
    {
        [Redact]
        public string Phone { get; init; } = string.Empty;

        public string Text { get; init; } = string.Empty;
    }

    [Test]
    public async Task StreamAsync_AMarkedValueOnEveryTypedPath_ReachesNoRecordButTheRunSeesIt()
    {
        var model = new ScriptedAgentModel(
            new AgentResponse
            {
                ToolCalls =
                [
                    new AgentToolCall("call-a", "book", $$"""{"guests":[{"Phone":"{{Phone}}","Name":"Ana"}]}"""),
                    new AgentToolCall("call-b", "look_up", "{}")
                ]
            },
            ScriptedAgentModel.Say("booked"));

        Guest[]? handed = null;
        var tools = new ToolKit("k")
            .AddTool("book", "Books a room.", tool => tool
                .ParamList<Guest>("guests", "Who stays.")
                .OnExecute(args =>
                {
                    handed = args.Get<Guest[]>("guests");
                    return ToolResult.Ok("booked");
                }))
            .AddTool("look_up", "Finds the booking.", () => ToolResult.Json(new Booking { Reference = "BK1", Phone = Phone }));

        var logs = new CollectingLoggerFactory();
        var tracer = new RecordingTracer();
        var workflow = new Workflow<State>("reach")
            .Job("ask", AgentJobFactory.Create<State>("ask", model)
                .WithPrompt(s => $"Book a room for the guest on {s.Phone}.")
                .WithTools(tools)
                .WithLogger(logs)
                .MapResult((s, text) => s with { Text = text })
                .Build())
            .Then("ask", Workflow.End)
            .UseTracing(tracer)
            .RecordContent(true);

        var written = new List<string>();
        await foreach (var evt in workflow.StreamAsync(new State { Phone = Phone }))
            written.Add(JsonSerializer.Serialize<object>(evt, AnankeJson.Record));

        written.ShouldContain(line => line.Contains("\"Phone\":\"[redacted]\""), "the state is written, masked");
        written.ShouldAllBe(line => !line.Contains(Phone));
        tracer.Spans.SelectMany(span => span.Attributes.Values).ShouldAllBe(value => !value.ToString()!.Contains(Phone));
        logs.Messages.ShouldAllBe(message => !message.Contains(Phone));

        model.Requests[0].Messages.ShouldContain(message => message.Content!.Contains(Phone), "the model is shown it");
        handed.ShouldNotBeNull().ShouldHaveSingleItem().Phone.ShouldBe(Phone, "the tool is handed it");
        model.Requests[^1].Messages.Last(message => message.Role == AgentRole.Tool).Content
            .ShouldNotBeNull().ShouldContain(Phone, customMessage: "the model is sent the result as it was");
    }

    private sealed class CollectingLoggerFactory : ILoggerFactory, ILogger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return [.. _messages]; }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }
}
