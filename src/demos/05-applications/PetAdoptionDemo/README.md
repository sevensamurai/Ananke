# 🐾 Happy Tails — Pet Adoption Demo

A full-stack demo for the **Ananke** framework showcasing stateful multi-phase AI conversations,
real-time streaming via SSE, mid-generation interrupts, human-in-the-loop payment, and multimodal
input (voice and photo).

---

## What it demonstrates

| Feature | Details |
|---|---|
| **Stateful phases** | A state machine drives the session through `Searching → Paperwork → Payment → Done` |
| **RAG / Knowledge base** | Shelter pets and policies are ingested from Markdown files and searched with vector embeddings |
| **Streaming** | Assistant responses stream token-by-token to the browser over SSE |
| **Real-time interrupts** | Type a new message while the agent is responding — it cancels mid-generation, incorporates the new input, and re-generates |
| **Human-in-the-loop** | The Payment phase pauses the agent and waits for the user to submit card details via a separate `/api/payment` endpoint |
| **Voice input** | Click 🎤 to record a voice message; sent as audio to the model |
| **Photo input** | Click 📷 to attach a photo — useful for queries like *"looking for a pet like this"* |
| **Multi-provider** | Runs on **OpenAI** (`gpt-4.1-mini`) or **Google Gemini** (`gemini-2.5-flash`) — swap with one config line |
| **Docker** | Single `docker compose up --build` starts all 5 services; API keys supplied via `.env` |

---

## Conversation phases

```
Searching ──[StartPaperwork]──► Paperwork ──[StartPayment]──► Payment ──[Complete]──► Done
Searching ──[Interrupt]──► Interrupted ──[Resume]──► Searching
```

- **Searching** — the agent browses the knowledge base, answers questions about pets, and triggers `start_adoption` when the user names a pet they want to adopt.
- **Paperwork** — collects the required information, walks through adoption requirements, and submits the application via tool call.
- **Payment** — emits a `payment_required` SSE event; the UI renders a card input form. Card details are handled exclusively in `/api/payment` and are never stored in session or history.
- **Interrupted** — fires when the user types while the agent is streaming. The in-flight generation is cancelled, the new message is injected into history, and the session resumes.

---

## Getting started

### 1. Get an API key

The demo defaults to **Google Gemini** (recommended — supports audio input natively):

1. Go to [Google AI Studio](https://aistudio.google.com/)
2. Create an API key under **Get API key**

For **OpenAI** instead, grab a key from [platform.openai.com](https://platform.openai.com/api-keys).

---

## Run everything in Docker _(recommended)_

The easiest way to run the demo. Docker builds the app and starts all five services
(Qdrant, Redis, MQTT, web app, payment service) together with proper health-check ordering.

### 1. Create `.env`

```bash
cp .env.example .env
```

Open `.env` and set your API key:

**Google Gemini:**
```env
PROVIDER=Google
GOOGLE_API_KEY=AIza...
```

**OpenAI:**
```env
PROVIDER=OpenAI
OPENAI_API_KEY=sk-...
```

### 2. Build and start

```bash
docker compose up --build
```

Then open **http://localhost:5033** in your browser.

To run in the background:
```bash
docker compose up --build -d
docker compose logs -f petadoption-web   # tail the web app
docker compose logs -f petadoption-payments
```

To stop everything:
```bash
docker compose down
```

To wipe Qdrant and Redis volumes (forces re-ingestion on next start):
```bash
docker compose down -v
```

---

## Run locally with `dotnet run`

If you prefer running the .NET processes directly, start the infrastructure containers first,
then run the two processes manually.

### 1. Create `secrets.json`

Create a `secrets.json` file in the `PetAdoptionDemo` project directory (it is gitignored):

**Google Gemini:**
```json
{
  "Provider": "Google",
  "Google": {
    "ApiKey": "YOUR_GOOGLE_API_KEY"
  }
}
```

**OpenAI:**
```json
{
  "Provider": "OpenAI",
  "OpenAI": {
    "ApiKey": "YOUR_OPENAI_API_KEY"
  }
}
```

You can also override the model:
```json
{
  "Provider": "Google",
  "Google": {
    "ApiKey": "YOUR_KEY",
    "Model": "gemini-2.5-pro"
  }
}
```

### 2. Start infrastructure only

```bash
docker compose up -d qdrant mqtt redis
```

This starts **Qdrant**, **MQTT** (Mosquitto), and **Redis** — without building or running the app containers.

### 3. Run the app

Open **two terminals** in the `PetAdoptionDemo` directory:

**Terminal 1 — Payment service:**
```bash
dotnet run -- --payment-service
```

**Terminal 2 — Web application:**
```bash
dotnet run
```

Then open **http://localhost:5033** in your browser.

---

## API endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/chat` | Send a message (text, audio, or image); returns SSE stream |
| `POST` | `/api/interrupt` | Interrupt a running generation with a new message |
| `POST` | `/api/payment` | Submit card details to advance through the Payment phase |

---

## Try it

- **"What dogs do you have?"** — searches the knowledge base
- **"I want to adopt Ziggy"** — triggers the Paperwork phase
- Type a new message **while the agent is responding** — triggers an interrupt and re-generation
- Click **▶ Demo** in the empty state for a scripted interrupt demonstration
- Click **📷** and attach a photo — *"looking for something like this"*
- Click **🎤** and record a voice question

---

## Project structure

```
PetAdoptionDemo/
├── Dockerfile              Multi-stage Docker build (context: solution root)
├── docker-compose.yml      All 5 services: Qdrant, Redis, MQTT, web app, payment worker
├── .env.example            Template for API key env vars (copy to .env)
├── mosquitto.conf          Mosquitto broker config (anonymous, port 1883)
├── appsettings.json        Default config (Mqtt, Redis, Qdrant host/port)
├── Program.cs              Web app entry point + startup wiring
├── Sessions/
│   ├── AdoptionMachine.cs  State machine: phases + transitions
│   ├── AdoptionSession.cs  Session: model + knowledge + message history
│   └── SessionFactory.cs   Wires phases onto a new session at creation
├── Knowledge/
│   ├── IngestionWorkflow.cs  Loads & indexes Markdown knowledge files into Qdrant
│   └── ShelterKnowledge.cs   Knowledge base section constants
├── Infrastructure/
│   ├── ProviderSettings.cs   Registers OpenAI + Google providers
│   └── MinimalConsoleFormatter.cs
├── Phases/
│   ├── SearchPhase.cs      RAG search + start_adoption tool
│   ├── InterruptPhase.cs   Interrupt handling + optional clarification
│   ├── PaperworkPhase.cs   Application requirements + submit tool
│   └── PaymentPhase.cs     HITL payment event
├── Endpoints/
│   ├── ChatEndpoint.cs     POST /api/chat
│   ├── InterruptEndpoint.cs POST /api/interrupt
│   └── PaymentEndpoint.cs  POST /api/payment
├── Models/                 Request/response record types
├── Services/
│   └── PaymentService.cs   Standalone MQTT payment worker (--payment-service)
├── data/                   Markdown knowledge files (pets, policies, care tips)
└── wwwroot/                Browser UI (vanilla JS + Pico CSS)
```

---

## Credits

**Colour palette** — [Coolors](https://coolors.co/264653-2a9d8f-e9c46a-f4a261-e76f51)
`#264653` · `#2A9D8F` · `#E9C46A` · `#F4A261` · `#E76F51`

**Corgi photo** — Photo by [Jarrel Ng](https://unsplash.com/@jarrelng?utm_source=unsplash&utm_medium=referral&utm_content=creditCopyText) on [Unsplash](https://unsplash.com/photos/a-dog-with-a-red-bandanna-around-its-neck-f8XqOR36gI4?utm_source=unsplash&utm_medium=referral&utm_content=creditCopyText)
