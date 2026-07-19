# Model Deprecations

Ananke tracks a lifecycle stage for every known model identifier so that stale references
warn instead of silently drifting. This page is the human-readable version of the same policy
encoded in `Models.cs` and both `ModelCatalog`s — see `src/Ananke.Design/README.md`
("Keeping the model catalog current") for the maintenance process.

## Lifecycle stages

| Stage | Meaning | Build-time signal | `ModelCatalog.Validate()` |
|---|---|---|---|
| **Current** | Recommended model for its tier — no newer replacement exists yet. | None. | `IsValid = true`, no message. |
| **Legacy** | Superseded by a Current model but still fully supported by the provider. | None. | `IsValid = true`, no message. |
| **Deprecated** | Provider has announced removal or is steering traffic away. Still callable today. | `[Obsolete]` (`ANNKE001`) at every constant reference; `ANNKE002` on an equal string literal. | `IsValid = true`, message names the replacement. |
| **Retired** | Provider no longer serves this model. Calls will fail. | `[Obsolete]` (`ANNKE001`); `ANNKE003` on an equal string literal. | `IsValid = false`, message names the replacement. |

In practice a Retired model does not sit in `Models.cs`/`ModelCatalog` long: once we confirm the
provider has actually shut it down, we remove the constant and its catalog entries entirely in
the same change (see "Removed models" below), rather than keeping a permanently-invalid constant
around for new code to trip over. `ModelStatus.Retired` and the `IsValid = false` branch above
exist for the (currently unused) window between "provider announced a firm retirement date" and
"we've actually deleted the constant."

`ANNKE001`, `ANNKE002`, and `ANNKE003` all fail the build like any other warning
(`TreatWarningsAsErrors` in `Directory.Build.props`) — referencing a deprecated or retired model
outside an annotated exception **is** a build break, not a nudge. This was a deliberate hardening
decision, made after leaving them as non-blocking warnings once let a solution-wide auto-fix sweep
introduce real regressions with nothing forcing a second look.

A handful of sites legitimately must keep referencing a deprecated identifier — both
`ModelCatalog` implementations (must remain resolvable for passthrough validation), each
provider's translation-table mappers (must keep resolving a deprecated-but-functional source
model), and family-name catalog keys that happen to string-equal a model id. Each such site wraps
the reference in `#pragma warning disable/restore ANNKE00x` with a one-line reason at the site —
grep the codebase for `disable ANNKE00` to see every intentional exception. Test fixtures are
exempted in bulk via `src/.editorconfig`'s `[tests/**/*.cs]` section rather than pragma-wrapped
individually, since a test deliberately constructing a deprecated model id as input data is
categorically different from production code drifting onto one.

## Current status (as of this page's last update — see `git log` for that date)

### Anthropic

| Model | Constant | Status | Replacement |
|---|---|---|---|
| `claude-sonnet-5` | `Sonnet5` | Current | — |
| `claude-fable-5` | `Fable5` | Current | — |
| `claude-haiku-4-5` | `Haiku45` | Current | — |
| `claude-sonnet-4-6` | `Sonnet46` | Legacy | `Sonnet5` |
| `claude-opus-4-8` | `Opus48` | Legacy | `Fable5` |
| `claude-opus-4-1` | `Opus41` | Deprecated | `Opus48` |

### OpenAI

| Model | Constant | Status | Replacement |
|---|---|---|---|
| `gpt-5.6-sol` | `Gpt56Sol` | Current | — |
| `gpt-5.6-terra` | `Gpt56Terra` | Current | — |
| `gpt-5.6-luna` | `Gpt56Luna` | Current | — |
| `gpt-5.5` | `Gpt55` | Legacy | `Gpt56Sol` |
| `gpt-5.4` | `Gpt54` | Legacy | `Gpt56Sol` |
| `gpt-5.4-mini` | `Gpt54Mini` | Legacy | `Gpt56Terra` |
| `gpt-5.4-nano` | `Gpt54Nano` | Legacy | `Gpt56Luna` |
| `gpt-5.2` | `Gpt52` | Deprecated | `Gpt56Sol` |
| `gpt-5` | `Gpt5` | Deprecated | `Gpt56Sol` |
| `gpt-5-mini` | `Gpt5Mini` | Deprecated | `Gpt56Terra` |
| `gpt-5-nano` | `Gpt5Nano` | Deprecated | `Gpt56Luna` |
| `gpt-4.1` | `Gpt41` | Deprecated | `Gpt56Sol` |
| `gpt-4.1-mini` | `Gpt41Mini` | Deprecated | `Gpt56Terra` |
| `gpt-4.1-nano` | `Gpt41Nano` | Deprecated | `Gpt56Luna` |
| `gpt-4o` | `Gpt4o` | Deprecated | `Gpt56Sol` |
| `gpt-4o-mini` | `Gpt4oMini` | Deprecated | `Gpt56Terra` |
| `o3` | `O3` | Deprecated | `Gpt56Sol` |
| `o3-mini` | `O3Mini` | Deprecated | `Gpt56Terra` |
| `o4-mini` | `O4Mini` | Deprecated | `Gpt56Terra` |

### Google

| Model | Constant | Status | Replacement |
|---|---|---|---|
| `gemini-3.5-flash` | `Gemini35Flash` | Current | — |
| `gemini-3.1-pro` | `Gemini31Pro` | Current | — (no GA 3.5 Pro successor yet) |
| `gemini-3.1-flash-image` | `Gemini31FlashImage` | Current | — |
| `gemini-3.1-flash-lite` | `Gemini31FlashLite` | Current | — |
| `gemma-4` | `Gemma4` | Current | — |
| `lyria-3` | `Lyria3` | Current | — |
| `gemini-3.1-flash` | `Gemini31Flash` | Legacy | `Gemini35Flash` |
| `gemini-2.5-pro` | `Gemini25Pro` | Deprecated | `Gemini31Pro` |
| `gemini-2.5-flash` | `Gemini25Flash` | Deprecated | `Gemini35Flash` |

### Removed models (Retired, then deleted)

These constants existed at earlier points but have been removed entirely — the provider has
confirmed the underlying model no longer serves requests, so keeping the constant around would
let new code reference a model guaranteed to fail. If you have code referencing one of these,
switch to its replacement.

| Removed model | Was constant | Provider retirement date | Replacement |
|---|---|---|---|
| `claude-opus-4` | `Opus4` | 2026-06-15 | `Fable5` |
| `claude-sonnet-4` | `Sonnet4` | 2026-06-15 | `Sonnet5` |
| `claude-3-5-sonnet` | `Sonnet35` | 2025-10-28 | `Sonnet5` |
| `claude-3-5-haiku` | `Haiku35` | 2026-02-19 | `Haiku45` |
| `gemini-2.0-flash` | `Gemini20Flash` | 2026-06-01 | `Gemini35Flash` |
| `gemini-2.0-flash-lite` | `Gemini20FlashLite` | 2026-06-01 | `Gemini31FlashLite` |

Two Orchestration-only templates (no `Models.cs` constant, so no compile-time trace) were removed
for the same reason: `Claude3_7Sonnet` (pinned to the now-retired dated snapshot
`claude-3-7-sonnet-20250219`, retired 2026-02-19) and `Claude3_5Haiku` (pinned to
`claude-3-5-haiku-20241022`, retired 2026-02-19).

Anthropic and Google publish retirement dates against specific dated snapshots
(`claude-opus-4-20250514`, `claude-sonnet-4-20250514`, `gemini-2.0-flash-001`, etc.); the bare,
undated aliases removed above (`claude-opus-4`, `claude-sonnet-4`, `claude-3-5-sonnet`,
`claude-3-5-haiku`) resolved to those snapshots as "latest release of this version" — with the
sole snapshot behind each alias retired, the alias has nothing left to resolve to.

## Updating this page

Whenever `Models.cs` or either `ModelCatalog`'s lifecycle table changes, update the table above
in the same PR. `ModelConstantsConformanceTests` verifies the *code* stays internally
consistent (every constant known, every non-Current template has a `ReplacedBy`); it does not
check this page, so it can drift — treat it as documentation, not as tested behavior.
