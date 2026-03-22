# ADR – Scaffold ServiceHost

**Date:** 2026-03-06
**Updated:** 2026-03-22
**Status:** Accepted
**Affected projects:** Scaffold.ServiceHost, Scaffold.ServiceHost.Abstractions

---

## Context

In the Scaffold Protocol human-in-the-loop pipeline model, the CLI is step-scoped — each `DevScaffold --step` invocation is an independent process. Loading models on CPU takes 30–60 seconds, so model lifecycle must not be tied to the CLI lifecycle. The ServiceHost solves this: it runs in the background, keeps models in memory, and receives CLI commands via Named Pipe.

---

## Decisions

---

### 1. ServiceHost as an independent background process

**Decision:** The ServiceHost is a standalone .NET console process, started by the CLI on the first `--step` invocation, and running until an explicit `DevScaffold shutdown` command is received.

**Rationale:**
- The CLI process is short-lived — model loading time would be unacceptable at CLI startup
- Keeping models in memory between steps is the ServiceHost's responsibility
- The ServiceHost lifecycle is independent of the CLI lifecycle — explicit shutdown is intentional

---

### 2. Two unidirectional Named Pipes — not one bidirectional pipe

**Decision:** Two separate `NamedPipeServerStream` instances — one command pipe (CLI → ServiceHost) and one event pipe (ServiceHost → CLI) — rather than a single bidirectional pipe.

**Rationale:**
- Two unidirectional pipes give clearer ownership: `PipeServer` reads the command pipe, `EventPublisher` writes the event pipe
- With a bidirectional pipe, read and write operations on the same stream can interfere
- The two pipes have independent, asynchronous lifetimes

**Pipe names:** `{pipeName}-commands` and `{pipeName}-events`

---

### 3. Protobuf framing — `WriteDelimitedTo` / `ParseDelimitedFrom`

**Decision:** Messages travel on the pipe with varint length-prefix framing (`WriteDelimitedTo` / `ParseDelimitedFrom`), not a fixed-length header or newline delimiter.

**Rationale:**
- Protobuf natively supports delimited framing — no custom protocol implementation needed
- The varint prefix is efficient: 1 byte overhead for small messages
- `CommandEnvelope` and `EventEnvelope` wrapper types provide a uniform frame — the pipe always carries the same type

---

### 4. Startup order: event pipe first, command pipe second

**Decision:** The ServiceHost first waits for the CLI to connect on the event pipe, sends `ServiceReadyEvent`, and only then opens the command pipe.

**Rationale:**
- On the CLI side, `WaitForReadyAsync` reads the event pipe directly, bypassing the event loop — this order guarantees that
- If the command pipe opened first, the CLI could send a command before the ServiceHost is ready to read
- `ServiceReadyEvent` is the only signal to the CLI that the ServiceHost is ready — the ordering makes this deterministic

---

### 5. Multi-session support — session loop in `PipeServer`

**Decision:** `PipeServer.RunAsync` contains an outer session loop. After each CLI session, `EventPublisher` resets the event pipe and waits for the next CLI connection.

**Rationale:**
- `NamedPipeServerStream` is not reusable after a connection is closed — a new instance is needed
- The ServiceHost serves multiple CLI processes in sequence due to the step-scoped CLI lifecycle
- `ModelCache` and `InferenceWorker` are session-independent — the reset affects only the pipe layer

**Session lifecycle:**
```
[session loop start]
  IPipeConnectionLifecycle.WaitForConnectionAsync()    → CLI connects
  IServiceEventPublisher.PublishServiceReadyAsync()
  PipeServer: open command pipe                         → CLI connects
  RunCommandLoopAsync()                                 → CLI exits → pipe closes
  commandPipe.DisposeAsync()
  IPipeConnectionLifecycle.ResetForNewConnectionAsync() → new pipe instance
[session loop restarts]
```

---

### 6. `EventPublisher` — `SemaphoreSlim` thread safety

**Decision:** `EventPublisher` uses a `SemaphoreSlim(1,1)` lock to ensure that only one `EventEnvelope` is written to the pipe at a time.

**Rationale:**
- The `InferenceWorker` progress timer and `ModelCache` status events can fire concurrently
- The pipe stream is not thread-safe — without a lock, messages could interleave
- `SemaphoreSlim` is an async lock — it does not block the thread, only the async continuation

---

### 7. `ModelCache` — lazy loading, per-alias lock, `IInferenceBackendFactory`

**Decision:** `ModelCache` uses lazy loading — a model is loaded on the first `GetOrLoadAsync` call. Thread safety is ensured by a per-alias `SemaphoreSlim`, not a global lock. Backend instantiation is delegated to `IInferenceBackendFactory` — `ModelCache` has no knowledge of whether LLamaSharp or an API backend is created.

**Rationale:**
- The ServiceHost starts immediately — no need to wait for model loading at startup
- Per-alias lock: if two different models need to be loaded concurrently, they do not block each other
- Double-check pattern: the cache is checked again inside the per-alias lock — avoids duplicate loading under race conditions

**Why `IInferenceBackendFactory`:**
- OCP: introducing a new backend type (e.g. gRPC-based remote inference) requires only a new factory implementation — `ModelCache` does not change
- `DefaultInferenceBackendFactory` owns the `HttpClient` shared by all `ApiInferenceBackend` instances
- The path-based type determination (`http://` prefix check) is a factory internal detail — it does not leak into the cache

**Loading order (per alias):**
```
_dictionaryLock → cache hit? → return
                → miss: per-alias lock
                  → _dictionaryLock (double-check) → cache hit? → return
                                                    → miss: IInferenceBackendFactory.CreateAsync
```

---

### 8. `ModelCache` event — delegate, not direct dependency

**Decision:** `ModelCache` notifies the event layer via `event Func<string, ModelStatus, string, Task>? ModelStatusChanged`, rather than holding a direct reference to `IServiceEventPublisher`.

**Rationale:**
- `ModelCache` does not depend on `IServiceEventPublisher` — testable in isolation
- `Program.cs` wires the two components together — the composition is in one place
- The event delegate is optional (`?`) — `ModelCache` is usable in tests without an event handler

---

### 9. `InferenceWorker` — fire-and-forget to protect the command loop

**Decision:** `CommandDispatcher` starts inference with `Task.Run` in a fire-and-forget fashion — it does not await it.

**Rationale:**
- Inference can take minutes — if `CommandDispatcher` awaited it, the command pipe would be blocked
- `CancelInferRequest` can only arrive if the command loop is running — so the command loop must not be blocked
- `InferenceWorker` sends `InferenceCompletedEvent` / `InferenceFailedEvent` itself

---

### 10. `InferenceWorker` — `SemaphoreSlim` to limit concurrent inference

**Decision:** `InferenceWorker` uses `SemaphoreSlim(1,1)` to ensure that only one inference runs at a time.

**Rationale:**
- On CPU-only hardware, parallel inference is not beneficial — both runs would slow down
- `InvalidOperationException` gives immediate feedback if the CLI incorrectly sends parallel requests
- Per-request cancel (TODO comment) can be introduced in the future without changing the current structure

---

### 11. Shutdown — `ShutdownToken` in `CommandDispatcher`

**Decision:** `CommandDispatcher` holds its own `CancellationTokenSource _shutdownCts`, whose `Token` is monitored by `PipeServer`. The `ShutdownRequest` handler cancels it — it does not directly stop the process.

**Rationale:**
- `CommandDispatcher` does not need to know how `PipeServer` stops — it only signals the intent
- `PipeServer` detects the shutdown signal via its linked token and exits cleanly
- Using async cancellation instead of `Environment.Exit` or `Process.Kill` is consistent and testable

---

### 12. Graceful shutdown — Ctrl+C and SIGTERM

**Decision:** `Program.cs` subscribes to `CancelKeyPress` and `ProcessExit` events and cancels the primary `CancellationTokenSource`.

**Rationale:**
- `ProcessExit` ensures `ModelCache.DisposeAsync` is called on SIGTERM as well
- The `using var cts` and `await using` dispose chain guarantees that `LLamaWeights` are released
- `OperationCanceledException` is caught at the top level and treated as a normal (0) exit

---

### 13. Output path — CLI provides, ServiceHost has fallback

**Decision:** The primary source for the output file path is `InferRequest.OutputFolder`, provided by the CLI. The ServiceHost uses it directly. If `OutputFolder` is empty (fallback), the ServiceHost derives the path from its own `--output` startup parameter.

**Filename within `OutputFolder`:**
```
{OutputFolder}/{stepId}_{requestId[..8]}.md
e.g. /output/MyProject/task_breakdown_2/task_breakdown_abc12345.md
```

**Rationale:**
- Generation number computation is the CLI's responsibility (filesystem-based) — the ServiceHost does not need to know about it
- The ServiceHost only receives the folder it should write to — it does not need to know the project structure
- The fallback (`--output`) provides backward compatibility and allows standalone ServiceHost testing
- The concrete file path is returned to the CLI via `InferenceCompletedEvent.output_file_path`, from where it goes into the audit log and human validation

---

## Interface segregation — `Scaffold.ServiceHost.Abstractions`

ServiceHost internal components communicate through interfaces — concrete implementations are only assembled in the composition root (`Program.cs`).

| Interface | Implemented by | Consumed by |
|---|---|---|
| `IInferenceEventPublisher` | `EventPublisher` | `InferenceWorker` |
| `IServiceEventPublisher` | `EventPublisher` | `PipeServer`, `CommandDispatcher` |
| `IEventPublisher` | `EventPublisher` | `CommandDispatcher` (both sub-interfaces) |
| `IPipeConnectionLifecycle` | `EventPublisher` | `PipeServer` |
| `IInferenceWorker` | `InferenceWorker` | `CommandDispatcher` |
| `IInferenceBackendProvider` | `ModelCache` | `InferenceWorker` |
| `IModelCacheManager` | `ModelCache` | `CommandDispatcher` |
| `IModelCache` | `ModelCache` | composition root |
| `IInferenceBackend` | `LlamaInferenceBackend`, `ApiInferenceBackend` | `InferenceWorker` (indirectly) |
| `IInferenceBackendFactory` | `DefaultInferenceBackendFactory` | `ModelCache` |

---

## Component summary

| Component | Single responsibility | Dependencies |
|---|---|---|
| `PipeServer` | Pipe lifecycle, session loop, command reading | `CommandDispatcher`, `IServiceEventPublisher`, `IPipeConnectionLifecycle` |
| `EventPublisher` | Event pipe writing, thread-safe sending | — (only `NamedPipeServerStream`) |
| `CommandDispatcher` | Command routing, shutdown signalling | `IInferenceWorker`, `IModelCacheManager`, `IEventPublisher` |
| `InferenceWorker` | Inference execution, progress events, output file writing | `IInferenceBackendProvider`, `IInferenceEventPublisher` |
| `ModelCache` | Lazy model loading, cache management | `ModelRegistryConfig`, `IInferenceBackendFactory` |
| `DefaultInferenceBackendFactory` | Backend instantiation (LLamaSharp / API decision) | `LLamaWeights`, `HttpClient` |

---

## Related ADRs

- **ADR-CLI-Refactor** — The thin client decision, which defines when the ServiceHost starts and stops, and that the CLI determines the output folder structure
- **ADR-Protocol** — The `InferRequest.output_folder` field rationale