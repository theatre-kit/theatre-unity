# Rule: Documentation Layout

> Foundation docs live in docs/, phase-specific designs in docs/designs/.

## Motivation

Theatre's documentation structure is simple and works: cross-cutting architecture docs at
`docs/` root, implementation-specific designs in `docs/designs/`. This two-level split keeps
the doc tree navigable without over-organizing. No per-subsystem READMEs are needed because
the design docs already cover subsystem details in context.

## Before / After

### From this codebase: Current layout (keep as-is)

```
docs/
├── ARCHITECTURE.md              — system design, threading, domain reload
├── STAGE-SURFACE.md             — Stage tool definitions
├── DIRECTOR-SURFACE.md          — Director tool definitions
├── CONTRACTS.md                 — wire format rules
├── UX.md                        — editor UI design
├── unity-threading-idioms.md    — concurrency patterns
└── designs/
    ├── phase-0-scaffold.md
    ├── phase-1-mcp-core.md
    ├── phase-2-scene-awareness.md
    ├── phase-3-spatial-queries.md
    ├── phase-4-watches-actions.md
    └── REFACTOR-PLAN.md
```

### Synthetic example: Over-organized docs (avoid)

**Anti-pattern:**
```
docs/
├── architecture/
│   ├── overview.md
│   ├── threading.md
│   └── domain-reload.md
├── api/
│   ├── stage/
│   │   ├── scene-snapshot.md
│   │   ├── scene-hierarchy.md
│   │   └── ...
│   └── director/
│       └── ...
├── guides/
│   └── ...
└── designs/
    └── ...
```

This creates too many small files and deep nesting. Foundation docs should be
comprehensive single files, not fragmented across subdirectories.

## Exceptions

- A new top-level doc is fine when it covers a cross-cutting concern (like CONTRACTS.md)
- REFACTOR-PLAN.md and similar operational docs belong in docs/designs/ alongside phase designs
- CLAUDE.md and similar agent/tool config files belong at repo root, not in docs/

## Scope

- Applies to: docs/ directory
- Does NOT apply to: Code comments, inline documentation, README.md at repo root
