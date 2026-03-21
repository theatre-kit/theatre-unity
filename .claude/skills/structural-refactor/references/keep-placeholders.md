# Rule: Keep Placeholder Structures

> Empty placeholder folders stay in the tree to document the planned roadmap.

## Motivation

Theatre's Director/ subsystem has 8 planned subdirectories documented in DIRECTOR-SURFACE.md.
Keeping empty folders (with .gitkeep) makes the roadmap visible in the file tree itself.
Developers can see at a glance what's implemented vs. planned without reading design docs.

## Before / After

### From this codebase: Director/ placeholders

**Current (keep as-is):**
```
Runtime/Director/
├── Scenes/        (.gitkeep — Phase 5)
├── Prefabs/       (.gitkeep — Phase 6)
├── Assets/        (.gitkeep — Phase 7)
├── Animation/     (.gitkeep — Phase 8)
├── Spatial/       (.gitkeep — Phase 9)
├── Input/         (.gitkeep — Phase 10)
└── Config/        (.gitkeep — Phase 11)
```

### Synthetic example: API module placeholders

**Preferred:**
```
API/
├── Users/         (implemented)
│   └── UserController.cs
├── Billing/       (.gitkeep — Q2 roadmap)
└── Analytics/     (.gitkeep — Q3 roadmap)
```

## Exceptions

- Don't create placeholders for speculative features that aren't in design docs
- Remove a placeholder if the feature is explicitly cancelled (not just deferred)
- Placeholders should only exist one level deep — don't pre-create nested subfolder trees

## Scope

- Applies to: Runtime/Director/ subdirectories, any future planned subsystems
- Does NOT apply to: Already-implemented subsystems (Stage/, Transport/, Core/)
