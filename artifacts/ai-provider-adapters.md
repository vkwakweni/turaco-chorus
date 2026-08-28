---
title: AI Provider Adapters
last-updated: 2026-08-28
---

# AI Provider Adapters

Design for `ClaudeInsightEngine` and `GeminiInsightEngine`, the two concrete adapters behind `IInsightEngine` (see `domain-interfaces-and-objects.md`). Both are genuine, interchangeable implementations of the same port, not a primary plus a stub.

## Why two

Gemini was implemented first over Claude because Claude API access needs its own developer account and prepaid credit, separate from a personal Claude.ai subscription — not something to set up on the spot. Gemini's free tier is ongoing, not a trial: 10 requests/minute, 250–1,500 requests/day depending on the Flash generation. Each `/ask` call makes two requests, so that's comfortably enough for personal testing and demos.

Rather than block on Claude credits, both adapters were built side by side — exactly the swap this port was designed to make cheap.

## Configuration switch

* Which adapter is active is a deployment decision, not a rebuild:
    * `InsightProvider` (`Claude` | `Gemini`) is read by `InsightProviderReader` and switched on in `AdapterRegistration.AddInsightEngine` (`src/TuracoChorus/AdapterRegistration.cs`), which registers only the selected adapter's options and `IInsightEngine` implementation.
    * Only the selected provider's API key needs a real value in config.

## Side by side

| | Claude | Gemini |
|---|---|---|
| Endpoint | `/v1/messages` | `/v1beta/models/{model}:generateContent` |
| Auth | `x-api-key` header | `x-goog-api-key` header |
| Default model | `claude-haiku-4-5` | `gemini-3.6-flash` |
| Structured output | Prompt-instructed JSON, defensively parsed (strips a markdown fence if the model adds one anyway) | Native — `generationConfig.responseMimeType: "application/json"` forces it; same defensive parser kept anyway |
| Free tier | None ongoing — a one-time ≈$5 trial credit | Ongoing, no card required |
| System prompts | Word-for-word identical between both adapters — same Ethics-by-Design constraints (neutral fact-reporting, no recommendations), same generic-dimension wording, same JSON contract for both `ExtractRangeAsync` and `AskAsync` | |

## One shared shape

Both adapters split into the same four pieces — pure prompt/parsing logic kept apart from the one place that actually makes an HTTP call, so most of each adapter is testable with no real API access at all:

1. **Options** (`*InsightEngineOptions.cs`) — `ApiKey`, `Model`, `BaseUrl` as a plain record, no client built in.
2. **Prompts** (`*Prompts.cs`) — the two fixed system prompts, pure string-building.
3. **Response parser** (`*ResponseParser.cs`) — JSON → `RequestedRange` / `Answer`, pure, no `HttpClient`.
4. **Adapter** (`*InsightEngine.cs`) — the one class that actually calls the API.

## When the provider says no

* This is the provider itself declining or truncating a response, checked before either adapter tries to extract an answer at all. Not every value gets its own message: anything other than the normal case throws `InsightResponseParseException` with the actual reason embedded, rather than a bespoke branch per value.
* Separate from the domain's own `"answerable": false` field

**Claude**: `stop_reason`, top-level, one per response:
| Value | Meaning |
|---|---|
| `end_turn` | normal completion |
| `refusal` | blocked by Claude's content classifier |
| `max_tokens` | truncated before completion |

(`stop_sequence`, `tool_use`, `pause_turn`, `model_context_window_exceeded` also exist but don't apply — neither adapter sets stop sequences or uses tools.)

**Gemini**: `promptFeedback.blockReason`, top-level, prompt blocked before any candidate:
| Value | Meaning |
|---|---|
| `SAFETY` / `PROHIBITED_CONTENT` / `BLOCKLIST` / `IMAGE_SAFETY` / `OTHER` | prompt rejected |

And `candidate.finishReason`, per candidate:
| Value | Meaning |
|---|---|
| `STOP` | normal completion |
| `SAFETY` / `PROHIBITED_CONTENT` / `RECITATION` / `OTHER` | blocked mid-generation |
| `MAX_TOKENS` | truncated before completion |

## The wider landscape

Providers considered before settling on Claude + Gemini as the two built adapters:

| Provider | Free tier | Status |
|---|---|---|
| Claude | One-time ≈$5 trial credit, no ongoing free tier | Built |
| Gemini | Ongoing — 10 RPM, 250–1,500 RPD on Flash models | Built |
| Groq | Ongoing — 30 RPM, 100K–500K tokens/day, no card | Not built |
| Mistral | ≈1B tokens/month on the Experiment tier | Requires opting into data training — a fit issue for an Ethics-by-Design project |
| OpenAI | No reliable free credit for new accounts | Not built |
| Ollama | Free, but local-only | Fargate has no GPU support; self-hosting means an EC2 GPU instance or a second cloud vendor (Railway, Fly.io) |

## Known limitations

- `ClaudeInsightEngine`'s integration test is written (mirrors Gemini's exactly) but not yet run against the real Messages API — no Claude API key exists yet, and getting one is deliberately deferred (see `roadmap.md`).
- Only one provider is active per deployment; there's no runtime fallback from one to the other if the configured provider's API is unavailable.
