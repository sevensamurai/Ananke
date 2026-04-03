using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Base type for events emitted by a streaming chat workflow.
/// Consumers iterate these via <see cref="StreamingChatWorkflow.Builder.BuildStream"/>.
/// </summary>
public abstract record ChatSessionEvent;

/// <summary>Incremental text streamed from the model.</summary>
public sealed record TextDeltaEvent(string Text) : ChatSessionEvent;

/// <summary>Incremental audio streamed from the model.</summary>
public sealed record AudioDeltaEvent(byte[] Data, string MimeType) : ChatSessionEvent;

/// <summary>The model is invoking a tool.</summary>
public sealed record ToolCallEvent(string Name, string Args) : ChatSessionEvent;

/// <summary>A tool execution completed.</summary>
public sealed record ToolResultEvent(string Name, string Result) : ChatSessionEvent;

/// <summary>The current generation was interrupted. <see cref="PartialText"/> contains any partial output.</summary>
public sealed record InterruptedEvent(string? PartialText) : ChatSessionEvent;

/// <summary>Generation resumed after an interrupt.</summary>
public sealed record ResumedEvent : ChatSessionEvent;

/// <summary>The conversation turn completed.</summary>
public sealed record CompletedEvent(string? FullText) : ChatSessionEvent;

/// <summary>An error occurred during the conversation.</summary>
public sealed record ErrorEvent(string Message) : ChatSessionEvent;
