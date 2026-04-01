# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the entire solution
dotnet build DevScaffold/DevScaffold.slnx

# Run CLI (executes one step, then exits)
dotnet run --project DevScaffold/Scaffold.CLI -- --step <step_name>

# Shut down the ServiceHost
dotnet run --project DevScaffold/Scaffold.CLI -- shutdown

# Run ServiceHost directly (normally auto-started by CLI)
dotnet run --project DevScaffold/Scaffold.ServiceHost
```

There are no automated tests in this repository yet. No lint step is configured.

## Architecture

This is a **human-in-the-loop AI development workflow orchestrator** — an LLM-powered assistant where a human validates every AI-generated output before it is accepted.

### Two-Process Design

The system is split into two independent executables:

- **Scaffold.CLI**: Thin, step-scoped client. One invocation = one step, then it exits (exit codes: 0 = accepted, 1 = error, 2 = rejected/escalated). Reads YAML config, assembles prompts, sends to ServiceHost, waits for events, runs validation, prompts human.
- **Scaffold.ServiceHost**: Persistent background process. Loads and caches LLM models in memory, executes inference, publishes events. Stays alive between CLI invocations.

### Inter-Process Communication

CLI and ServiceHost communicate via two unidirectional **Named Pipes**:
- `{pipe-name}-commands`: CLI → ServiceHost (CommandEnvelope)
- `{pipe-name}-events`: ServiceHost → CLI (EventEnvelope)

Messages are serialized with **Protocol Buffers** using varint length-prefix framing. Every request carries a `request_id` (GUID) for async correlation. Protobuf definitions live in `Scaffold.Agent.Protocol/protos/`.

### Clean Architecture Layers

| Layer | Projects |
|---|---|
| Domain | `Scaffold.Domain` — value objects, config models, enums |
| Application | `Scaffold.Application` — orchestration, interfaces, use-case logic |
| Infrastructure | `Scaffold.Infrastructure.ConfigHandler` — YAML parsing, input assembly |
| ServiceHost | `Scaffold.ServiceHost` + `Scaffold.ServiceHost.Abstractions` — inference, model cache, event publishing |
| Validation | `Scaffold.Validation.Abstractions`, `Scaffold.Validation`, `Scaffold.Validation.Steps` |
| Protocol | `Scaffold.Agent.Protocol` — generated from `.proto` files |
| Presentation | `Scaffold.CLI` — composition root, console, pipe client, editor launcher |

All major components are abstracted by interfaces and composed via `Microsoft.Extensions.DependencyInjection`. Composition roots are `Program.cs` files in CLI and ServiceHost.

### Validation Pipeline

Two layers run automatically before human review is triggered:

1. **Universal validator** — detects stop tokens, truncation, token-limit proximity
2. **Per-step validators** — structural and constraint checks (e.g., `TaskBreakdownValidator`)

When validation fails, a targeted refinement prompt is retried automatically (up to the configured attempt limit) — the human is only involved after automatic validation passes. See `scaffold_validation_principles.md` for the philosophy on validator scope.

### Configuration (YAML)

Multi-layer YAML config:
- **CLI project config** (`Scaffold.CLI.yaml`) — pipe name, output directory, model aliases, step agent configs
- **Model registry** — maps aliases to backend type (LLamaSharp local or OpenAI-compatible API) and model paths
- **Step agent configs** — per-step system prompt, input references, validator settings
- **Input YAML** — project-specific inputs assembled by `InputAssembler`

### Key Abstractions to Know

- `IInferenceBackend` / `IInferenceBackendFactory` — swap local (LLamaSharp/GGUF) vs. remote (OpenAI-compatible API) at runtime
- `IHumanValidationService` — Accept / Edit / Reject interaction (console implementation in CLI)
- `IFileEditorLauncher` — cross-platform editor launch (defaults to OS file association)
- `IScaffoldConsole` — three-level color-coded output: CLI (cyan), Session (gray), Validation (yellow), Error (red)
- `IAuditLogger` — fixed-width plain-text audit log written per-generation to `{output}/{project}/{step}_{generation}/audit.log`

### Extensibility

- **New step**: add YAML step agent config (+ optional `IStepOutputValidator` registered in DI) — no other code changes needed
- **New validator**: implement `IStepOutputValidator`, register in DI
- **New inference backend**: implement `IInferenceBackend`, update `IInferenceBackendFactory`
- **New UI**: implement `IHumanValidationService`

## ADRs

Architecture Decision Records document all major design choices and should be consulted before modifying core communication or orchestration logic:
- `Scaffold.Agent.Protocol/ADR-Protocol.md` — protobuf message design, framing, correlation
- `Scaffold.CLI/ADR-CLI.md` — CLI lifecycle, pipe client, generation numbering, audit log, console colors
- `Scaffold.ServiceHost/ADR-ServiceHost.md` — startup, model cache, fire-and-forget inference, graceful shutdown
