---
name: stylistic-refactor
description: >
  Project stylistic refactoring rules for Unity C#. Proactively scans for refactoring
  opportunities and produces a prioritized backlog. Defines Theatre's preferred coding style.
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Agent, Write
---

# Stylistic Refactor

Scan the codebase for opportunities to apply these stylistic preferences.
Each style has a reference file with rationale, examples, and exceptions.

## Styles

| Style | Rule (one line) | Reference |
|-------|-----------------|-----------|
| Result Types | Use result structs for expected failures; reserve exceptions for unrecoverable situations | [details](references/result-types.md) |
| LINQ Cold Paths | Use LINQ for readability in tool handlers and editor code; foreach in hot paths | [details](references/linq-cold-paths.md) |
| Static Stateless | Prefer static classes for stateless operations; only use instances when state is needed | [details](references/static-stateless.md) |
| Guard Clauses | Validate preconditions at the top of methods; return early; max 2-3 nesting levels | [details](references/guard-clauses.md) |

## Output

Write the refactoring backlog to `docs/stylistic-refactor-backlog.md`.

The document should be a **prioritized refactoring backlog** with three tiers:

### High Value
Refactors that significantly improve readability, consistency, or maintainability
with low risk. Each entry: file path, current code snippet, proposed change, rationale.

### Worth Considering
Valid refactors with moderate impact or moderate effort. Include rationale.

### Not Worth It
Code that technically violates a style but should NOT be refactored. Include WHY:
too destructive, too complex for marginal gain, would obscure domain logic, breaks
API contracts, or forces unnatural patterns. We want a unified feel, not refactoring
for refactoring's sake.

Focus on code that benefits from the change — skip trivial or cosmetic-only improvements.
