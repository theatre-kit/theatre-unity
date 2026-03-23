---
name: patterns
description: "Theatre Unity code patterns. Auto-loads when implementing, designing,
  reviewing, or adding new tools. Provides structural patterns with concrete code
  examples for tool registration, response building, thread dispatch, and domain
  reload survival."
user-invocable: false
allowed-tools: Read, Glob, Grep
---

# Theatre Unity — Code Patterns Reference

Structural patterns for the Theatre Unity MCP server. Each file contains a named pattern with rationale, 3-4 concrete examples with file:line references, and common violations to avoid.

## Available Patterns

- [tool-handler.md](tool-handler.md) — Static class + schema + Register/Execute structure for every MCP tool
- [compound-tool-dispatch.md](compound-tool-dispatch.md) — Switch on `"operation"` → sub-handler classes for multi-operation tools
- [response-building.md](response-building.md) — ResponseHelpers usage for frame context, identity, vectors, errors
- [budget-and-truncate.md](budget-and-truncate.md) — TokenBudget loop with WouldExceed/Add/ToBudgetJObject for variable-size responses
- [action-sub-handler.md](action-sub-handler.md) — Action sub-op structure: RequirePlayMode → ResolveFromArgs → Undo → mutate → AddIdentity + AddFrameContext
- [spatial-index-sub-handler.md](spatial-index-sub-handler.md) — Spatial query pipeline: ParseVector3 → SpatialEntryFilter → GetIndex().Query → SpatialResultBuilder → budget
- [physics-mode-dispatch.md](physics-mode-dispatch.md) — PhysicsMode.GetEffective() → Execute2D/Execute3D branches for physics-based queries
- [main-thread-dispatch.md](main-thread-dispatch.md) — MainThreadDispatcher.Invoke for marshaling HTTP thread → Unity main thread
- [domain-reload-survival.md](domain-reload-survival.md) — [InitializeOnLoad] + SessionState persistence for surviving Unity recompiles
- [dry-run-support.md](dry-run-support.md) — DirectorHelpers.CheckDryRun() for validate-without-mutate in all Director tools
- [test-fixture.md](test-fixture.md) — Temp directory setup, CallTool helpers, and resource tracking for integration tests
