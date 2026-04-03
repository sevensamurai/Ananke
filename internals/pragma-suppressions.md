# Pragma & Warning Suppressions Tracker

Track all `#pragma warning disable` and other suppressions introduced in the codebase.
Review these when upgrading vendor SDK packages — suppressions may become unnecessary
or the underlying APIs may have changed.

---

## Active Suppressions

| Warning ID | File | Reason | Vendor Package | Since |
|---|---|---|---|---|
| `OPENAI001` | `Ananke.Orchestration.OpenAI/OpenAIChatAgentModel.cs` | `ChatInputAudioFormat` is marked experimental in the OpenAI SDK. Used in `MapAudioFormat()` and the `AudioPart` case in `MapMessages()`. | `OpenAI` 2.9.1 | Phase 2 (ADR-002) |

## Review Checklist

When upgrading a vendor SDK package:

1. Search for its warning IDs in this file
2. Check if the experimental/preview API has been stabilized
3. If stabilized: remove the `#pragma` pair and verify build
4. If changed: update the calling code to match the new API
5. Update this file accordingly

## Cleared Suppressions

| Warning ID | File | Cleared In | Notes |
|---|---|---|---|
| *(none yet)* | | | |
