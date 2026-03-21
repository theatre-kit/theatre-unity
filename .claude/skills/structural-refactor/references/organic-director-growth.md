# Rule: Organic Director Growth

> Director/ structure evolves with its code, not forced into Stage/'s shape.

## Motivation

Stage/ observes game state (read-only spatial queries, hierarchy walks). Director/ mutates
it (scene changes, prefab instantiation, asset creation, animation). These are fundamentally
different domains with different complexity profiles. Forcing Director/ into Stage/'s
GameObject/Spatial/ECS grouping would be artificial — Director's natural grouping is by
Unity subsystem (Scenes, Prefabs, Assets, Animation, etc.).

## Before / After

### Synthetic example: Forced symmetry (avoid)

**Anti-pattern:**
```
Runtime/
├── Stage/
│   ├── GameObject/     — hierarchy, inspection, watches
│   ├── Spatial/        — spatial index, clustering, budget
│   └── ECS/            — DOTS queries
└── Director/
    ├── GameObject/     — forced mirror of Stage (wrong!)
    ├── Spatial/        — forced mirror of Stage (wrong!)
    └── ECS/            — forced mirror of Stage (wrong!)
```

**Preferred:**
```
Runtime/
├── Stage/
│   ├── GameObject/
│   ├── Spatial/
│   └── ECS/
└── Director/
    ├── Scenes/         — scene load/unload, hierarchy mutation
    ├── Prefabs/        — instantiation, variant creation
    ├── Assets/         — material, texture, SO creation
    └── Animation/      — clip, controller, timeline editing
```

### From this codebase: Current Director/ placeholders

The existing placeholder structure already follows subsystem grouping:
```
Runtime/Director/
├── Scenes/
├── Prefabs/
├── Assets/
├── Animation/
├── Spatial/        (tilemap, terrain, navmesh — not Stage's spatial)
├── Input/
└── Config/
```
This is correct. When implementing, let each subsystem's internal structure emerge from the code.

## Exceptions

- If Director/ and Stage/ share a common pattern (e.g., both need a shared utility), extract
  it to a common location rather than duplicating
- If a Director subsystem is trivial (< 3 files), it can stay flat without internal subfolders

## Scope

- Applies to: Runtime/Director/ and its future implementation
- Does NOT apply to: Runtime/Stage/ (already established), Editor/Tools/ (has its own rule)
