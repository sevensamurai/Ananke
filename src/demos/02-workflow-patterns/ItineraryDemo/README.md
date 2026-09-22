# ItineraryDemo

A week in Japan, planned around one place that has to be there — and steered when the service says
that place cannot be had the way the plan first asked for it.

## What the run shows

The demo recommends the best plan for a trip under a set of constraints.

The plan (`plan/trip.yml`) asks for a week in Japan with Hakone in it, arriving and leaving on fixed
dates. Its only step finds two nights in a row in Hakone; nothing else in the file says which other
places to visit or how many nights each gets — that is left for the run to work out.

A step searches the service (`plan/service.json`) and reports whether it found what it was asked for,
with every stay it found. The loop marks the step from that report:

- one stay that fits: the step is done, with that stay;
- several stays that fit: the supervisor picks one (attended, a person picks), and the step is done;
- nothing that fits: the supervisor keeps the stays that come close, and the choice goes to the
  Planner, which changes the plan.

`Validate` rules on the service and the plan, never on a step's word: `stay(...)` asks whether some nights
are free between two dates, `stay_on(...)` whether exact nights are, `week_planned(...)` whether the
plan's `stay_on` steps add up to the week, and `follows_route()`
whether they visit places in the order the service's route gives (Tokyo → Hakone → Kyoto → Osaka),
out along it and, on a round trip, back along it once — never out again. The Planner looks the route
up.

Under the fixed week, Hakone's onsen only has one night free, so the step cannot find the two nights
it was asked for. It says so, and lists what it found instead — one night on Sunday 4 April, in the
middle of the week. The Planner keeps Hakone at that one night and plans the nights before and after
it, in route order.

Run the flexible plan (`--plan plan/trip-flexible.yml`) and the same halt happens for a different
reason: Hakone is free from 13 April, near the end of a two-week window. The Planner puts Hakone where
it is actually free and plans the rest of the window around it.

Either way the console ends with the recommended stays, and `run.log` next to it holds the same
lines.

## Prerequisites

The demo needs a real model to author and run the plan — nothing is scripted. Set one of the
following in a `.env` file at the repository root (`.env.example` lists every variable):

```
OPENAI_API_KEY=sk-...        (or)        GOOGLE_API_KEY=...
```

Optionally pin the model ids with `OPENAI_MODEL` / `GEMINI_MODEL`, or per role with
`ANANKE_DEMO_EXECUTOR_MODEL` and `ANANKE_DEMO_SUPERVISOR_MODEL`.

## Commands

Run from the repository root.

Let the demo pick whichever provider has a key set (OpenAI first if both are present), unattended —
the run takes the supervisor's recommendation itself:

```powershell
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo
```

Choose a provider explicitly:

```powershell
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- openai
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- google
```

Put a person at the pause instead:

```powershell
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- --hitl
```

Run the flexible week instead of the fixed one:

```powershell
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- --plan plan/trip-flexible.yml
```

Verify the scenario itself — that the plan still admits, and that Hakone still cannot be had the way
it is first asked for — without needing a model key:

```powershell
dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- --verify
```

## Flags

| Flag | Effect |
| --- | --- |
| `openai` \| `google` \| `auto` | Chooses the provider (`auto` is the default). |
| `--hitl` | Puts a person at each pause instead of taking the supervisor's recommendation inline. |
| `--plan <file>` | Which plan file to load, relative to the built demo (default `plan/trip.yml`). |
| `--verify` | Checks the plan and the service without running a model. |

## Reading a run

The console shows the shape of the run and clips long sentences a model wrote. Everything else is in
`runs/<timestamp>/`:

- **`run.log`** has the console's lines unclipped, in order, plus one line for every tool call:
  `tool <agent>: <tool> <arguments> → <result>`. The agent is `plan-node:<step>` for a step,
  `plan-advisor` for the supervisor and `trip-planner` for the Planner. When the Planner writes
  nothing, the line before `planner wrote nothing` says why: out of tool rounds, no answer, or an
  answer with no steps. `planner's plan does not hold` means `check_plan` rejected the Planner's
  answer; the line says whether the Planner was asked again or nothing was written.
- **The last lines** say how the run ended: done, given up, something broke, stopped by the
  change-of-plan ceiling (and which version never ran), or stopped at a step, with `Why it stopped:`.
  A run that did not finish then prints the plan as it ended, with each step's state.
- **`events.jsonl`** has the same run as data, one event per line. For example, every tool call:

  ```bash
  jq -c 'select(.type == "AgentToolCalled") | .event | [.AgentName, .ToolName, .Arguments, .Result]' runs/<timestamp>/events.jsonl
  ```

## Files

| File | What it is |
| --- | --- |
| `plan/trip.yml` | The fixed-week plan. |
| `plan/trip-flexible.yml` | The same trip over a wider, flexible window. |
| `plan/service.json` | The route places lie along, what each place offers, and the nights a stay is free. |
| `runs/<timestamp>/run.log` | Everything the run printed, unabridged. |
| `runs/<timestamp>/itinerary.json` | The recommended stays, one entry per step that is done. |
| `runs/<timestamp>/events.jsonl` | Every plan event and tool call, one JSON object per line. |
