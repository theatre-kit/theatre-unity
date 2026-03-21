---
name: structural-refactor
description: >
  Project structural organization rules for Unity C# (UPM package). Proactively scans for
  organizational issues and produces a prioritized backlog. Defines the team's preferred file,
  folder, and module structure.
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Agent, Write
---

# Structural Refactor

Scan the codebase for organizational issues based on these structural rules.
Each rule has a reference file with rationale, examples, and exceptions.

## Rules

| Rule | Summary | Reference |
|------|---------|-----------|
| domain-grouped-tools | Group Editor/Tools/ into domain subfolders when >15 files | [details](references/domain-grouped-tools.md) |
| namespace-folder-alignment | Namespace must mirror folder path exactly | [details](references/namespace-folder-alignment.md) |
| shared-utility-subfolder | Cross-cutting utilities live in dedicated Shared/ subfolders | [details](references/shared-utility-subfolder.md) |
| keep-placeholders | Empty placeholder folders stay to document roadmap | [details](references/keep-placeholders.md) |
| feature-grouped-tests | Test files cover feature areas, not 1:1 with source files | [details](references/feature-grouped-tests.md) |
| file-size-cap | Split files exceeding 500 lines of code | [details](references/file-size-cap.md) |
| organic-director-growth | Director/ structure evolves with its code, not forced into Stage/ shape | [details](references/organic-director-growth.md) |
| docs-layout | Foundation docs in docs/, phase designs in docs/designs/ | [details](references/docs-layout.md) |

## Output

Write the refactoring backlog to `docs/structural-refactor-backlog.md`.

The document should be a **prioritized refactoring backlog** with three tiers:

### High Value
Structural changes that significantly improve navigability with low risk.
Each entry: current structure, proposed change, rationale, affected files.

### Worth Considering
Valid reorganizations with moderate impact or moderate effort.

### Not Worth It
Code that technically violates a rule but should NOT be reorganized.
Include WHY: too many dependents, churn outweighs benefit, etc.
