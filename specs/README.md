# specs/ – Implementation Task Files

This folder is the working record of how DevScaffold was built.

## How the work is run

1. **Design first.** An architectural change is discussed until the decisions are
   clear, then recorded in the relevant ADR.
2. **Split into tasks.** The change is broken into small YAML task files with explicit
   `depends_on` ordering. Each task names the files to create or modify, the intended
   change, and verifiable `acceptance_criteria`.
3. **AI executes, human verifies.** Claude Code executes one task at a time; the human
   reviews the diff and runs the acceptance checks before the next task starts.
4. **Live test.** After a task set is complete, the feature is exercised end to end
   with real models.

This is the Scaffold Protocol applied to its own development: the human orchestrates,
the AI executes.

## Task file format

```yaml
task_id: "05"
title: "..."
depends_on: ["01"]
description: |        # why, and the decisions already made
files_to_create: []   # path + content or precise spec
files_to_modify: []   # path + change
acceptance_criteria: [] # checked by the human before moving on
```

## Folders

| Folder | Scope |
|---|---|
| `reference-readiness/` | Reference-readiness hardening for public review: config hygiene, path guard, failure path, tests, CI, documentation, review follow-ups. See `reference-readiness/_index.md` for the task order. |
| `coder-step-tasks/` | Post-processing pipeline, artifact extraction, `--input`, `--apply` (the fourth CLI refactor round, see ADR-CLI). |

## Language

Task files are written in Hungarian – they are the author's original working
documents and are kept unedited as an authentic record. Code, ADRs and all
user-facing documentation are in English.
