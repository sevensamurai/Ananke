# AgentToAgentProtocolDemo

A self-contained demo that exposes an Ananke agent as an **A2A-compliant HTTP server** and calls it from two independent clients — a C# client using the Ananke SDK, and a Python client using only the standard library.

> **A2A** is for agent-to-agent communication. **MCP** is for agent-to-tool communication. Together they cover the full interoperability spectrum.

---

## What the demo shows

| Concept | How |
|---|---|
| **A2A server** | An Ananke `Workflow` + `ToolKit` exposed as a JSON-RPC HTTP endpoint |
| **Agent card** | Automatic skill discovery via `GET /.well-known/agent-card.json` |
| **C# client** | `A2AAgentModel` — calls the remote agent as a drop-in `IStreamingAgentModel` |
| **Python client** | Plain HTTP + JSON-RPC, no Ananke SDK, no third-party packages |
| **Cross-language interop** | Any language that speaks HTTP + JSON-RPC can call any A2A agent |

---

## Architecture

```
┌─────────────────────────────────────┐
│         A2A Server (port 5120)      │
│                                     │
│  EchoAgent                          │
│  ├── ToolKit ("text")               │
│  │   ├── word_count                 │
│  │   ├── reverse                    │
│  │   └── uppercase                  │
│  └── Workflow<PipelineState>        │
│      validate → enrich → format     │
│                                     │
│  GET  /.well-known/agent-card.json  │
│  POST /a2a  (JSON-RPC 2.0)          │
└───────────────┬─────────────────────┘
                │  HTTP
       ┌────────┴────────┐
       │                 │
┌──────▼──────┐   ┌──────▼──────┐
│  C# Client  │   │Python Client│
│             │   │             │
│A2AAgentModel│   │ stdlib only │
│IAgentModel  │   │ no pip deps │
└─────────────┘   └─────────────┘
```

The key point: the Python client has **no dependency on Ananke**. It talks to the server using nothing but HTTP and JSON-RPC — exactly how any external system would.

---

## The agent

`EchoAgent` exposes two capabilities through a single A2A endpoint:

### Tools — dispatched by the `command: argument` prefix

| Tool | Input | Output |
|---|---|---|
| `word_count` | `word_count: some text` | `N words` |
| `reverse` | `reverse: Ananke` | `eknanA` |
| `uppercase` | `uppercase: hello` | `HELLO` |

### Pipeline — any other input runs the 3-step workflow

```
validate → enrich → format
```

- **validate** — checks the input is non-empty, sets `IsValid` / `Status`
- **enrich** — counts words and characters
- **format** — produces `[N words, M chars] INPUT-IN-UPPERCASE`

---

## Run modes

```bash
cd src

# Both server and client in one process (default)
dotnet run --project demos/AgentToAgentProtocolDemo

# Server only — listens on http://localhost:5120
dotnet run --project demos/AgentToAgentProtocolDemo -- --server

# C# client only — connects to http://localhost:5120
dotnet run --project demos/AgentToAgentProtocolDemo -- --client
```

---

## Python client

Demonstrates calling the server from Python with zero framework dependencies:

```bash
# 1. Start the server
dotnet run --project demos/AgentToAgentProtocolDemo -- --server

# 2. In another terminal, run the Python client (Python 3.9+)
cd demos/AgentToAgentProtocolDemo
python python_client/a2a_client.py

# Optional: override the base URL
python python_client/a2a_client.py http://localhost:5120
```

The client performs the same three steps as the C# client:

1. **Discovery** — `GET /.well-known/agent-card.json` — reads the agent card
2. **Message/send** — `POST /a2a` JSON-RPC — sends pipeline input and tool commands
3. **Interop note** — prints what this illustrates about the A2A protocol

No `pip install`, no virtual environment, no Ananke SDK. Just Python 3.9+.

---

## What the C# client demonstrates

### Step 1 — Agent discovery

```csharp
var discovery = new A2AAgentDiscovery();
var info = await discovery.DiscoverAsync(new Uri(serverUrl));

Console.WriteLine(info.Name);           // "Ananke Echo Agent"
Console.WriteLine(info.SupportsStreaming);
foreach (var skill in info.Skills) ...
```

### Step 2 — Sending messages via `A2AAgentModel`

```csharp
var agentModel = new A2AAgentModel(new A2AAgentModelOptions
{
    AgentUrl = new Uri("http://localhost:5120/a2a")
});

// Pipeline — plain text runs the workflow
var response = await agentModel.GenerateAsync(new AgentRequest
{
    Messages = [AgentMessage.User("Hello from the A2A client!")]
});
// → "[3 words, 27 chars] HELLO FROM THE A2A CLIENT!"

// Tool dispatch — "command: argument" format
response = await agentModel.GenerateAsync(new AgentRequest
{
    Messages = [AgentMessage.User("reverse: Ananke")]
});
// → "eknanA"
```

### Step 3 — `A2AAgentModel` as `IStreamingAgentModel`

`A2AAgentModel` implements `IStreamingAgentModel`, so it plugs into any Ananke workflow, `AgentJob`, or model router exactly like a local OpenAI / Anthropic / Google model — the rest of the framework has no idea it is talking to a remote agent over HTTP.

---

## What the Python client demonstrates

```python
# Discovery — standard GET
card = get_json("http://localhost:5120/.well-known/agent-card.json")
print(card["name"], card["version"])

# Send a message — plain JSON-RPC POST
payload = {
    "jsonrpc": "2.0",
    "id": str(uuid.uuid4()),
    "method": "message/send",
    "params": {
        "message": {
            "role": "user",
            "messageId": str(uuid.uuid4()),
            "parts": [{"kind": "text", "text": "Hello from Python!"}],
        }
    },
}
response = post_json("http://localhost:5120/a2a", payload)
```

This is the raw wire format the C# `A2AAgentModel` produces internally. Seeing both side by side makes the protocol transparent.

---

## Project structure

```
AgentToAgentProtocolDemo/
├── Program.cs             — Entry point, run-mode dispatch, server + client logic
├── EchoAgent.cs           — ToolKit, Workflow<PipelineState>, agent card, task adapter
├── A2AEndpoints.cs        — ASP.NET Core JSON-RPC endpoint wiring
├── python_client/
│   └── a2a_client.py      — Python client, stdlib only, no Ananke dependency
└── README.md              — This file
```
