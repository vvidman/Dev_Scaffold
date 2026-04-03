# Dev_Scaffold

**Scaffold Protocol** is a human-in-the-loop, YAML-based, step-by-step development workflow for .NET projects. The goal is to enable an experienced senior or lead developer to carry out development tasks in a controlled, auditable manner — within a competitive development timeframe.

**Core principle: AI is a tool, not an orchestrator.** Every step's output goes through human validation before it is used as input to the next step. The human decides when each step runs, evaluates the output, and either accepts, edits, or rejects it with a clarification for regeneration.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  Developer machine                                           │
│                                                             │
│  DevScaffold CLI ──── Named Pipe ────► Scaffold.ServiceHost │
│       │               (protobuf)            │               │
│       │                                     │               │
│   Human review                        LLM inference         │
│   (editor opens)                      (LLamaSharp / API)    │
└─────────────────────────────────────────────────────────────┘
```

The system has two independent processes:

- **`Scaffold.CLI`** (`DevScaffold`) — a thin client. It loads YAML configs, assembles the prompt, sends an inference request, waits for the result, runs automatic validation, and presents the output for human review. It exits after each step.
- **`Scaffold.ServiceHost`** — a persistent background process. It loads and caches LLM models, executes inference, writes output files, and streams progress events back to the CLI. It stays running between CLI invocations to avoid repeated model loading.

Communication is via two unidirectional Named Pipes using [Protocol Buffers](https://protobuf.dev/):
- `{pipe-name}-commands` — CLI → ServiceHost
- `{pipe-name}-events` — ServiceHost → CLI

---

## Project Structure

```
Scaffold.Agent.Protocol/       protobuf definitions (commands, events, models, inference)
Scaffold.Domain/               domain models (ModelConfig, ValidationOutcome, etc.)
Scaffold.Application/          application layer (ScaffoldStepOrchestrator, RefinementStrategy,
│                               InferenceResultHandler, interfaces)
Scaffold.Application.Interfaces/  abstractions (IPipeClient, IHumanValidationService,
│                                  IAuditLogger, IScaffoldConsole, IFileEditorLauncher, etc.)
Scaffold.Validation/           output validators (UniversalOutputValidator, TaskBreakdownValidator,
│                               CompositeOutputValidator, StepValidatorRegistry)
Scaffold.Validation.Abstractions/  (IOutputValidator, IStepOutputValidator, ValidatorRuleSet)
Scaffold.Infrastructure/       YAML readers (YamlStepAgentConfigReader, InputAssembler, etc.)
Scaffold.ServiceHost/          inference host (InferenceWorker, ModelCache, EventPublisher,
│                               PipeServer, CommandDispatcher, DefaultInferenceBackendFactory)
Scaffold.ServiceHost.Abstractions/  ServiceHost interfaces (IInferenceWorker, IEventPublisher,
│                                    IModelCache, IInferenceBackendFactory, etc.)
Scaffold.CLI/                  CLI entry point, ConsoleHumanValidationService,
                                DefaultFileEditorLauncher, ServiceHostLauncher
```

---

## Prerequisites

- .NET 9 SDK
- A GGUF model file (e.g. [Qwen2.5-7B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF)) **or** an OpenAI-compatible API endpoint
- Windows, macOS, or Linux

---

## Configuration

### 1. CLI project config — `Scaffold.CLI.yaml`

Place this file next to the `DevScaffold` executable. The filename must match the executable (e.g. `Scaffold.CLI.exe` → `Scaffold.CLI.yaml`).

```yaml
host_binary_path: ./bin/Scaffold.ServiceHost
models:           ./models.yaml
pipe_name:        MyProject
output:           ./output
project_context:  ./input.yaml

steps:
  task_breakdown:
    input_config:    ./steps/task_breakdown_agent.yaml
    model_alias:     qwen2.5-7b-instruct

  coding:
    input_config:    ./steps/coding_agent.yaml
    model_alias:     qwen2.5-coder-7b-instruct
```

| Field | Description |
|---|---|
| `host_binary_path` | Path to the `Scaffold.ServiceHost` binary |
| `models` | Path to the model registry YAML |
| `pipe_name` | Named pipe identifier — must be unique per project |
| `output` | Root directory for step outputs |
| `project_context` | Path to the shared input YAML passed to every step |
| `steps.<name>.input_config` | Path to the step agent YAML (system prompt, max_tokens) |
| `steps.<name>.model_alias` | Which model alias to use for this step |
| `project_root` | Target directory for --apply. Required only when using --apply. |

### 2. Model registry — `models.yaml`

```yaml
models:
  qwen2.5-7b-instruct:
    path: D:/models/qwen2.5-7b-instruct-q4_k_m.gguf
    context_size: 8192
    gpu_layer_count: 0

  qwen2.5-coder-7b-instruct:
    path: D:/models/qwen2.5-coder-7b-instruct-q4_k_m.gguf
    context_size: 8192
    gpu_layer_count: 0

  gpt-4o-via-api:
    path: https://api.openai.com/v1
    api_key_env: OPENAI_API_KEY
    model_id: gpt-4o
```

For **local GGUF models**, `path` is a file system path. The ServiceHost uses LLamaSharp for inference.

For **OpenAI-compatible API backends**, `path` is an `http://` or `https://` URL. The ServiceHost sends chat completion requests. Any OpenAI-compatible endpoint works (OpenAI, Azure OpenAI, Ollama, LM Studio, vLLM, etc.).

### 3. Step agent config — e.g. `task_breakdown_agent.yaml`

```yaml
step: task_breakdown
max_tokens: 4096
system_prompt: |
  You are a senior software architect. Your task is to break down
  the provided feature description into a detailed, implementable
  task list following the project's architectural constraints.
  
  Rules:
  - Each task must reference exactly one affected file
  - Do not modify interfaces marked as closed for modification
  - Output must be a numbered markdown list
```

### 4. Input YAML — `input.yaml`

The input YAML is passed to every step. It can contain direct values and path references. All files referenced by fields ending in `path` are inlined into the prompt automatically by `InputAssembler`.

```yaml
project_name: Expense Tracker
feature_description: |
  Add monthly budget limits per category with overspend alerts.

architecture_path: ./docs/architecture.md
existing_code_path: ./src/ExpenseTracker.Core/Services/BudgetService.cs
constraints_path:   ./docs/architectural-constraints.md
```

---

## Usage

### Running a step

```bash
Scaffold.CLI --step task_breakdown
Scaffold.CLI --step coding
Scaffold.CLI --step review
```

The CLI will:
1. Start the ServiceHost if it is not already running
2. Send the inference request
3. Wait for the model to generate output (progress shown every 30 seconds)
4. Run automatic validation (structure, constraints, token limits)
5. Open the output file in your default editor
6. Ask for your decision: **Accept**, **Edit**, or **Reject**

On **Reject**, you provide a clarification. The CLI regenerates with the clarification appended to the system prompt — up to 5 attempts before escalating to human review.

### Shutting down the ServiceHost

```bash
Scaffold.CLI shutdown
```

### Help

```bash
Scaffold.CLI --help
```

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Step accepted or edited — proceed to next step |
| `1` | Error — connection failure, parse error, or unexpected exception |
| `2` | Rejected by human — regeneration needed |

Exit code `2` is intentionally distinct from `1` so shell scripts can distinguish a rejection from a failure.

---

## Output Structure

Each `Scaffold.CLI --step <name>` invocation creates a new generation folder:

```
output/
  MyProject/
    task_breakdown_1/
      task_breakdown_abc12345.md   ← generated output
      audit.log                    ← full audit trail
    task_breakdown_2/              ← after first rejection
      task_breakdown_def67890.md
      audit.log
    coding_1/
      coding_xyz99887.md
      audit.log
      artifacts/
        src/Services/FooService.cs
```

### Audit log format

```
2026-03-22 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=3
2026-03-22 14:23:01.124 [INFO ] [CONFIG          ] model=qwen2.5-7b-instruct system_prompt_length=342 max_tokens=4096
2026-03-22 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
2026-03-22 14:25:50.012 [INFO ] [VALIDATION      ] outcome=Reject clarification="More granular task breakdown needed"
2026-03-22 14:25:50.013 [INFO ] [SESSION_END     ] total_elapsed=169s outcome=Reject attempts=1
```

The format is human-readable and machine-parseable (fixed-width timestamp, level, and tag columns).

---

## Validation

Outputs go through two layers of automatic validation before reaching the human:

**Universal validator** — applies to all steps:
- Detects stop token leakage (`<|im_end|>`, `<|endoftext|>`, etc.)
- Detects truncated output (no meaningful ending)
- Warns on token limit proximity (>90% of `max_tokens` used)

**Per-step validator** — step-specific structural and constraint checks:
- Registered via `IStepOutputValidator` — each validator declares its `StepId`
- Loaded from `StepValidatorRegistry`
- Optional declarative rules loaded from `{step}_validator.yaml`

If automatic validation fails, the CLI builds a targeted refinement prompt listing the specific violations and fix hints, and reruns without human intervention. Human review is only triggered when validation passes.

---

## Shell Script Automation

Because exit codes are deterministic, steps can be chained in shell scripts:

```bash
#!/bin/bash
set -e

Scaffold.CLI --step task_breakdown
if [ $? -eq 2 ]; then
  echo "Task breakdown was rejected. Run again after reviewing."
  exit 2
fi

Scaffold.CLI --step coding
Scaffold.CLI --step review

Scaffold.CLI shutdown
```

---

## Applying Generated Artifacts

After accepting a coding step, generated files are staged in the `artifacts/` folder.
To copy them into your actual project:

```bash
DevScaffold --apply coding_1
DevScaffold --apply coding_1 coding_2    # batch
DevScaffold --apply coding_1 --dry-run   # preview only
```

**Important:** Apply always overwrites existing files in `project_root` without prompting.
Review the artifact contents before applying.

Configure `project_root` in `Scaffold.CLI.yaml`:
```yaml
project_root: ../MyActualProject/
```

---

## Extensibility

### Adding a new step

1. Create a step agent YAML with `step`, `max_tokens`, and `system_prompt`
2. Add the step to `Scaffold.CLI.yaml` under `steps`
3. Optionally create a `{step}_validator.yaml` with declarative rules
4. Optionally implement `IStepOutputValidator` for structural validation

No code changes are required for new steps unless you want custom validation logic.

### Adding a new per-step validator

Implement `IStepOutputValidator` and register it in the DI container:

```csharp
public class MyStepValidator : IStepOutputValidator
{
    public string StepId => "my_step";

    public IReadOnlyList<ValidationViolation> Validate(
        string outputContent,
        ValidatorRuleSet? ruleSet)
    {
        var violations = new List<ValidationViolation>();

        if (!outputContent.Contains("## Summary"))
            violations.Add(new ValidationViolation(
                ruleId: "MISSING_SUMMARY",
                severity: ViolationSeverity.Error,
                description: "Output must contain a ## Summary section.",
                fixHint: "Add a ## Summary section at the end of the output."));

        return violations;
    }
}
```

Register in `Program.cs`:
```csharp
services.AddSingleton<IStepOutputValidator, MyStepValidator>();
```

### Adding a new inference backend

Implement `IInferenceBackend` and update `DefaultInferenceBackendFactory.CreateAsync`:

```csharp
public async Task<IInferenceBackend> CreateAsync(
    ModelConfig config,
    CancellationToken cancellationToken = default)
{
    if (IsGrpcEndpoint(config.Path))
        return new GrpcInferenceBackend(config);

    return IsApiEndpoint(config.Path)
        ? new ApiInferenceBackend(config, _httpClient)
        : await LlamaInferenceBackend.LoadStatelessAsync(config, cancellationToken);
}
```

### Replacing the human validation UI

Implement `IHumanValidationService` and register it:

```csharp
public class WebHumanValidationService : IHumanValidationService
{
    public async Task<ValidationDecision> ValidateAsync(
        string stepId, string outputFilePath)
    {
        // open a local web UI, return the decision
    }
}
```

### Replacing the file editor

Implement `IFileEditorLauncher` to use a specific editor:

```csharp
public class VsCodeEditorLauncher : IFileEditorLauncher
{
    public bool TryOpen(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("code", filePath)
            {
                UseShellExecute = false
            });
            return true;
        }
        catch { return false; }
    }
}
```

---

## Supported Models

Any model in GGUF format supported by [LLamaSharp](https://github.com/SciSharp/LLamaSharp), and any OpenAI chat-completion compatible API.

Tested with:
- `Qwen2.5-7B-Instruct` (task breakdown, review steps)
- `Qwen2.5-Coder-7B-Instruct` (code generation steps)

Recommended quantization: `Q4_K_M` for a good balance of speed and quality on CPU.

---

## License

Apache License 2.0 — see [LICENSE](LICENSE) for details.