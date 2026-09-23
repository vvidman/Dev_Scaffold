# ADR – Output Validation: Principles and Approach

**Date:** 2026-03-18
**Updated:** 2026-09-23
**Status:** Accepted
**Affected projects:** Scaffold.Validation, Scaffold.Validation.Steps, Scaffold.Validation.Abstractions, Scaffold.Application (RefinementStrategy, InferenceResultHandler)

---

## Context

An LLM output is **never deterministic in the mathematical sense.** Temperature, sampling, and the model's internal state mean the same input can produce two different outputs across two runs. This is a fact, and no validation framework changes it.

The goal is therefore not full determinism of the output. The goal is that:

- **outputs that are certainly wrong can be filtered out automatically,**
- **variance stays within an acceptable range,**
- **errors can be detected, flagged, and fixed in a targeted way.**

This is analogous to software testing: tests do not guarantee the code is bug-free – but they guarantee that **certain classes of bugs do not get through.** The same logic applies here.

---

## Decisions

---

### 1. Validators check necessary conditions, not sufficient ones

Validators check **necessary conditions**, not sufficient conditions.

| | Automatable | Not automatable |
|---|---|---|
| **Example** | Structure, constraint compliance, stop token, Roslyn compile (planned) | Architectural correctness, business logic, content fit |
| **If it fails** | Output is certainly wrong | – |
| **If it passes** | Output *may* be right | Human decides |

> **Golden rule:** If the validator returns a failure, the output is certainly wrong.
> If it returns a pass, the output may be right – but the human validates it.

This keeps the human's attention focused on the **real questions**, not on mechanical checks.

---

### 2. Only automate what is certainly wrong

#### Too strict a validator → false-positive rejections

If the forbidden-keyword list or the field-order check is too rigid, the system keeps rejecting outputs that are actually correct. The refinement loop falls into an endless cycle. This is **more frustrating and more harmful than lax validation.**

**Principle:** Only automate a check when you can be certain the thing it flags is wrong.

---

### 3. No LLM-as-judge for content correctness

The LLM-as-judge approach is tempting, but it is **not a reliable judge** of content correctness – it is itself an LLM, itself variant. Building automation on top of this layer gives a misleading sense of safety.

**Principle:** An LLM judge may only be used to route UNCERTAIN cases to the human – never as the decision-maker.

---

### 4. Incremental validation infrastructure

If the validation infrastructure is built ahead of the actual functionality, development effort goes to the wrong place. The first two implementation steps (UniversalValidator + TaskBreakdownValidator) already deliver value on their own.

**Principle:** Build incrementally – every step must be independently shippable and testable.

---

### 5. Error-driven refinement, not retry

The traditional approach: Reject → human writes a clarification → rerun. This is **a retry**, not a fix.

Error-driven refinement instead works like this:

```
Validator detects a concrete violation
        ↓
RefinementPromptBuilder → targeted error message into the next run's prompt
        ↓
The LLM receives exactly the error it needs to avoid
        ↓
Rerun with targeted context
```

**Example refinement prompt addition:**
```
Previous attempt violations:
  - [FORBIDDEN_AFFECTED_FILE] Task 3 lists IRepository.cs as affected file.
    Fix: IRepository<T> is closed for modification. Remove this task or
    redirect the change to CachingRepository.cs.
  - [CACHE_INVALIDATION_IN_SERVICE] Task 7 places cache logic in ProductService.cs.
    Fix: Cache invalidation must be in CachingRepository<T> Add/Update/Delete methods.
```

The human clarification is still used for errors that the automatic validator cannot turn into a violation – but the system handles the mechanical errors on its own.

---

### 6. Violations are data – quality becomes measurable

Every run, every violation, and every ValidationOutcome is **data**. The system is not static – it can be iterated on based on the model's observed behavior:

```
Run → Violation log → Validator rule refinement → Next run
```

Aggregated measurement across multiple runs:

```
task_breakdown | STOP_TOKEN_LEAKED      | 3/5 runs → infrastructure bug
task_breakdown | FORBIDDEN_KEYWORD      | 2/5 runs → prompt refinement needed
task_breakdown | TOKEN_LIMIT_PROXIMITY  | 5/5 runs → max_tokens increase needed
```

This is the measurement basis that makes the effect of prompt iterations **quantifiable** – instead of saying "it feels better" you can say the violation rate went down.

The given LLM's context therefore grows richer run over run: the validator rules and the refinement prompts together form the knowledge base that steers the model into an increasingly tight range – without ever modifying the model itself.

---

## Validation layers

```
Output
  │
  ▼
[Universal validator]        ← stop token, truncation, token-limit proximity
  │
  ├── Error → Auto Reject + violation log + refinement prompt
  │
  └── Pass
        │
        ▼
  [Per-step validator]        ← structure, constraints, forbidden keywords, Roslyn (planned)
        │
        ├── Error → Auto Reject + targeted FixHint for the refinement
        │
        └── Pass / Warning only
              │
              ▼
        [Human validation]    ← content correctness, architecture, business logic
              │
              ▼
        ValidationOutcome + full log
```

---

## What this does not solve

It is important to state plainly that the validation framework **does not guarantee** a correct output. It does not replace human judgment on content questions. It does not make the system mathematically deterministic.

What it does solve: **it directs human attention to the real questions**, and it makes the system's quality measurable over time. A project that relies purely on human intuition to judge output does not scale. This framework solves that – not determinism.

---

## Implementation status

| Element | Status |
|---|---|
| Universal validator (empty output, stop token, truncation, token-limit proximity) | Implemented – `UniversalOutputValidator` |
| Per-step structural validator | Implemented for `task_breakdown` – `TaskBreakdownValidator` |
| Declarative rules from YAML | Implemented – `ValidatorYamlReader`, `ValidatorRuleSet` |
| Error-driven refinement with `FixHint` and `[AUTO]` prefix | Implemented – `RefinementStrategy` |
| Violation logging | Implemented – per-generation `audit.log` (key=value, parser-friendly) |
| Roslyn compile check for generated C# | **Planned** – not implemented |
| Violation-rate aggregation across runs | **Planned** – data is in `audit.log`; no aggregation tooling yet |
| LLM judge for routing UNCERTAIN cases to the human | **Not planned** as a decision-maker (see Decision 3) |

---

## Related ADRs

- [ADR-CLI](../Scaffold.CLI/ADR-CLI.md) – #12 (rejected output is not fed back), audit log (#17)
- [ADR-Protocol](../Scaffold.Agent.Protocol/ADR-Protocol.md) – `InferenceCompletedEvent.tokens_generated` (the token-limit proximity check's input)
