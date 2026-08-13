---
title: Ethics by Design
last-updated: 2026-08-13
---

# Ethics by Design

Turaco Chorus was developed using the **Ethics by Design for AI (EbD-AI)**, first coined by Brey and Dainow in their landmark paper, _Ethics by design for artificial intelligence_ (2023)^[https://doi.org/10.1007/s43681-023-00330-4]. EbD provides a model for system design that includes ethical considerations throughout the whole development process, particularly with the advent of artificial intelligence model implementations in applications processing sensitive user data.

Given that Turaco Chorus will function as a third-party microservice for user data aggregation with an opt-in AI service, it's important that these ethical requirements are well-considered from the beginning, not as patchwork.

The following document describes a translation of EbD's abstract tracts into implementation decisions for Turaco Chorus.

## 1. Human agency

**Assessment:** Not violated — `/ask` is informational only; it has no ability to act on the user's behalf.

**Design requirements:**
- Turaco Chorus never takes an action for the user or makes a decision on their behalf. `ILogDataSource` is read-only by construction (`domain-interfaces-and-objects.md`) — there is no write path from `/ask` back into the user's data, so the AI cannot alter anything even if it wanted to.
- The system prompt (see `domain-interfaces-and-objects.md`'s `IInsightEngine` section) instructs the AI to answer the question asked, not to recommend actions, nudge behaviour, or imply the user should do something — consistent with EbD-AI's requirement that AI "may recommend, but the final decision must always be made by a human," pushed even further here: it doesn't recommend at all.

## 2. Privacy and data governance

**Assessment:** Not violated — provided the requirements below are followed (this is the value the rest of the project's design work has been most oriented around).

**Design requirements:**
- **Data boundary:** only aggregates leave the service boundary to the AI provider, never raw entry text. This isn't a runtime check — `AggregateStats` structurally has no field capable of holding raw text (`domain-interfaces-and-objects.md`).
- **Consent model:** default-off, explicit opt-in, revocable. `IConsentStore` is checked before every `/ask` call, including before the range-extraction step — a denial short-circuits before any data reaches the AI provider (`interaction-flows.md`).
- **Withdrawal takes effect immediately, retroactively for future calls:** `PUT /consent {granted: false}` blocks all subsequent `/ask` calls right away. It does not delete prior `AuditEntry` records — audit history is append-only (see Accountability, below) — so withdrawal affects future data flow, not the historical record of past flow.
- **Least-privilege access to the source data:** `ILogDataSource`'s adapter reads via a scoped, read-only credential — never write access, never broader than the aggregates it needs.
- **Auditability of data handling:** every `/ask` call — granted or denied — is recorded via `IAuditLogger` (see Accountability, below), satisfying EbD-AI's requirement that data acquisition and use be auditable by humans.

## 3. Fairness

**Assessment:** Largely not applicable in the classic sense (no trained model, no demographic input, no ranking or scoring of people) — but one concrete requirement still applies.

**Design requirements:**
- Turaco Chorus has no ML model of its own and no training data to be biased — `IInsightEngine` wraps a general-purpose LLM called per-request over one user's own aggregated data, not a system that learns from or discriminates between users.
- What *does* apply: every user is treated identically by the system's own logic. Consent gating, audit logging, and the data boundary apply uniformly — there's no special-casing by user, account age, or data volume. This is the fairness requirement translated to Turaco Chorus's scale: equal treatment by the service itself, since there's no model behaviour to audit for bias.

## 4. Individual, social and environmental well-being

**Assessment:** Not violated — provided the requirements below are followed, covering both response neutrality and environmental impact at scale.

**Design requirements:**
- A service that compares a user's own habits (e.g. "did I read more books than movies watched this month?") can easily produce answers that read as judgmental about the user's own behaviour, even unintentionally. The system prompt must instruct the AI to report facts neutrally, not editorialise or imply value judgments about what the user's data "should" look like.
- Environmental impact: out of scope at this project's scale — a low-traffic portfolio service calling a hosted API, no infrastructure decisions here meaningfully move this needle. Noted here rather than left unaddressed.

## 5. Transparency

**Assessment:** Not violated — provided the requirements below are followed (explainability is the central requirement here).

**Design requirements:**
- **Explainability:** every `/ask` answer cites what data was used to produce it. `Answer.dataUsed` is not an optional add-on — it's part of the domain object's shape (`domain-interfaces-and-objects.md`), so every response necessarily carries this.
- **Users must know they're talking to an AI:** the `/ask` endpoint is explicitly and only that — an AI query endpoint, never presented as a human-reviewed or automatically-verified feature. If the deferred frontend integration (see `roadmap.md`'s "Later" section) surfaces this in Logger's World's UI, that UI must also make this explicit — flagged here as a requirement to carry forward when that work starts, not to solve now.
- **Auditability of the AI's own operation:** system prompt content, and the fact that two AI calls occur per `/ask` request (range extraction, then answering), are documented in `domain-interfaces-and-objects.md` and `interaction-flows.md` — satisfying the requirement that the system's internal process be traceable, not just its output.

## 6. Accountability and oversight

**Assessment:** Not violated — provided the requirements below are followed (contingent on the audit-log schema — the next roadmap item — actually implementing what's described here).

**Design requirements:**
- `IAuditLogger.RecordAuditEntryAsync` is called once per `/ask` request, regardless of outcome — including consent denials — so there is a complete record of every attempt to use the AI feature, not just successful ones (`domain-interfaces-and-objects.md`, `interaction-flows.md`).
- Audit records are append-only and survive consent revocation (see Privacy, above) — human oversight requires the historical record to be tamper-resistant, not editable after the fact.
- The audit log must be human-readable, not just machine-parseable, so a human reviewer (not just another system) can audit what happened — this is a concrete constraint on the schema design in the next roadmap item.
- No component in this system is unowned: `ILogDataSource`, `IInsightEngine`, `IConsentStore`, and `IAuditLogger` are each a single, well-defined port with a single adapter responsible for it — accountability for a failure in any one of them traces to exactly one place.
