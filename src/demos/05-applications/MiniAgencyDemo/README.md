# MiniAgencyDemo

MiniAgencyDemo is a Slack-backed application demo that runs a small two-stage agency:

- a drafter role generates the first response
- an LLM reviewer checks the draft
- a short reaction window allows a human in the Slack channel to add a `:white_check_mark:` or `:x:` on the review prompt

The demo is intentionally practical rather than fully declarative. `roles.json` and `build-and-review.ananke.yml` are shipped with the project and wired through `StudioHostBuilder`, while the runtime orchestration lives in `MiniAgencyMessageHandler` so the review loop works against the APIs that exist in this repo today.

## Prerequisites

- A Slack app with Socket Mode enabled
- Scopes: `app_mentions:read`, `channels:history`, `chat:write`, `chat:write.customize`, `chat:write.public`, `groups:history`, `im:history`, `mpim:history`, `reactions:read`, `reactions:write`
- A local OpenAI-compatible model endpoint such as Ollama, LM Studio, or vLLM

## Configuration

Set these values with environment variables or a local `secrets.json` copied next to the project output.

- `SLACK_BOT_TOKEN`: bot token (`xoxb-...`)
- `SLACK_APP_TOKEN`: app token (`xapp-...`) when Socket Mode is enabled
- `ANANKE_LOCAL_ENDPOINT`: local OpenAI-compatible endpoint, for example `http://localhost:11434/v1`
- `ANANKE_LOCAL_MODEL`: local model name, for example `llama3.2:3b`
- `ANANKE_LOCAL_API_KEY`: optional API key for the endpoint. For Ollama, any non-empty string works.

Optional demo settings:

- `MiniAgency__BudgetCap`: rolling token cap for the in-memory budget gate
- `MiniAgency__BudgetWindowMinutes`: rolling window for the budget gate
- `MiniAgency__HumanReviewTimeoutSeconds`: how long the Slack reaction window stays open
- `MiniAgency__EnableBudgetMetrics`: when `true`, the demo emits federation-style token counters. The demo still works without any metrics pipeline.

## Run

```powershell
dotnet run --project src/demos/05-applications/MiniAgencyDemo/MiniAgencyDemo.csproj
```

Mention the bot in Slack or send it a message in a channel it can read. The demo replies in a thread, opens a short review window, and posts the review outcome back into the same thread.

## Notes

- Budgeting works out of the box with `InMemoryBudgetMeter`; metrics are optional and disabled by default.
- The shipped YAML file is an artifact for the studio/roles layer, not a separate executable runtime for the Slack review loop.
