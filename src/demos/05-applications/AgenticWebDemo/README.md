# AgenticWebDemo — Streaming Chat & Human-in-the-Loop Web App

A minimal ASP.NET Core web application that shows how to embed Ananke into an HTTP service. It exposes two agentic features: a **streaming stock-market chat** and a **trade-approval workflow** that pauses for human sign-off before executing.

---

## Quick Start

```bash
cd demos/AgenticWebDemo
# Populate secrets.json (see below)
dotnet run
```

Open `http://localhost:5000` in a browser.

---

## Secrets

Create `secrets.json`:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4.1-mini"
  },
  "BetterStack": {
    "OtlpEndpoint": "https://in-otel.logs.betterstack.com",
    "OtlpSourceToken": "<optional — omit to disable tracing>"
  }
}
```

`BetterStack` keys are optional. Without them the app runs normally but OpenTelemetry tracing is disabled.

---

## What the Demo Shows

### Streaming Chat (`POST /api/chat`)

- `StreamingChatWorkflow` drives a tool-calling agent over Server-Sent Events (SSE).
- `StockTools` exposes `get_stock_price`, `get_stock_fundamentals`, `get_market_news`, `buy_shares`, and `sell_shares` to the model.
- Conversation context is preserved across turns with `IConversationMemory`.
- The front-end renders token deltas as they arrive.

### Trade Approval — Human-in-the-Loop (`POST /api/trade/analyze` + `POST /api/trade/approve`)

1. `/api/trade/analyze` starts a `TradeApprovalWorkflow`, which streams analysis then **interrupts** (`WorkflowStatus.Interrupted`) returning an `executionId`.
2. A human reviews the analysis in the UI and clicks Approve or Reject.
3. `/api/trade/approve` resumes the checkpointed workflow with the human decision; execution continues and the final result streams back.

| API | Purpose |
|---|---|
| `POST /api/chat` | Streaming stock-market chat (SSE) |
| `POST /api/trade/analyze` | Analyze a trade and pause for approval |
| `POST /api/trade/approve` | Resume a paused trade workflow |

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | ASP.NET Core entry point; wires services and registers endpoints |
| `AgentConfig.cs` | Reads `secrets.json`, builds `IStreamingAgentModel` |
| `AgenticApplication.cs` | Core chat handler — context strategy, middleware, SSE emission |
| `ChatEndpoint.cs` | `POST /api/chat` route registration |
| `ChatModels.cs` | `ChatRequest` / `ChatResponse` DTOs |
| `StockTools.cs` | Mock stock market `ToolKit` (prices, fundamentals, buy/sell) |
| `TradeApprovalWorkflow.cs` | Interrupt / resume workflow for trade sign-off |
| `TradeApprovalEndpoint.cs` | `POST /api/trade/analyze` and `/approve` route registration |
| `TradeApprovalModels.cs` | DTOs for trade analysis and approval requests |

---

## Infrastructure

| Dependency | Required | Notes |
|---|---|---|
| OpenAI API key | ✓ | Any OpenAI-compatible endpoint works |
| BetterStack OTLP token | Optional | Enables distributed tracing |

---

## Related

- Guide: [05 — Streaming Chat](../../../../docs/guides/05-streaming-chat.md)
- Guide: [07 — Human-in-the-Loop](../../../../docs/guides/07-human-in-the-loop.md)
- Guide: [10 — Observability](../../../../docs/guides/10-observability.md)
- Package: [Ananke.Orchestration](../../../Ananke.Orchestration/README.md)
- Package: [Ananke.OpenTelemetry](../../../Ananke.OpenTelemetry/README.md)
- Category page: [05 — Applications](../../../../docs/demos.md)
