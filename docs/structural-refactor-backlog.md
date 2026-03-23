# Structural Refactor Backlog

Prioritized list of organizational improvements based on the project's
structural rules. Each entry includes current state, proposed change,
rationale, and affected files.

Last scanned: 2026-03-22

---

## High Value

### 1. Fix namespace misalignment in Editor/Tools/ root files

**Rule**: namespace-folder-alignment
**Current**: 5 files in `Editor/Tools/` declare `namespace Theatre.Editor`
instead of `Theatre.Editor.Tools`.
**Proposed**: Change namespace to `Theatre.Editor.Tools` in each file.
Update all `using Theatre.Editor;` references to these types across
Editor/ and Tests/ assemblies.

| File | Current | Should Be |
|------|---------|-----------|
| `Editor/Tools/ConsoleLogBuffer.cs` | `Theatre.Editor` | `Theatre.Editor.Tools` |
| `Editor/Tools/TestSceneCreator.cs` | `Theatre.Editor` | `Theatre.Editor.Tools` |
| `Editor/Tools/TheatreStatusTool.cs` | `Theatre.Editor` | `Theatre.Editor.Tools` |
| `Editor/Tools/UnityConsoleTool.cs` | `Theatre.Editor` | `Theatre.Editor.Tools` |
| `Editor/Tools/UnityTestsTool.cs` | `Theatre.Editor` | `Theatre.Editor.Tools` |

**Rationale**: These 5 files sit alongside `CompoundToolDispatcher.cs`
(which already uses `Theatre.Editor.Tools`). The misalignment means
`using Theatre.Editor;` pulls in both root Editor types and Tool types
with no way to distinguish. Fixing this costs a find-and-replace on
`using` statements plus test file updates.

**Effort**: Low (namespace rename + using updates)
**Risk**: Low (compile errors from missing usings are immediately caught)

---

### 2. Split oversized Director tool files (>500 LOC)

**Rule**: file-size-cap
**Current**: 7 Director files exceed 500 lines:

| File | Lines |
|------|-------|
| `Director/TerrainOpTool.cs` | 777 |
| `Director/SceneOpHandlers.cs` | 709 |
| `Director/AnimatorControllerOpTool.cs` | 680 |
| `Director/NavMeshOpTool.cs` | 663 |
| `Director/TilemapOpTool.cs` | 653 |
| `Director/DirectorHelpers.cs` | 635 |
| `Director/PrefabOpHandlers.cs` | 511 |

**Proposed**: Extract sub-handler methods into separate files following
the compound tool pattern. For example:
- `TerrainOpTool.cs` (777 LOC) → `TerrainOpTool.cs` (dispatcher, ~80 LOC)
  + `TerrainOpHandlers.cs` (operation handlers)
- `AnimatorControllerOpTool.cs` (680 LOC) → same split pattern
- `DirectorHelpers.cs` (635 LOC) → extract `PropertyValueHelpers.cs`
  for the `ReadPropertyValue`/`SetPropertyValue`/`ResolveObjectReference`
  block (~250 LOC)

Files at 511 LOC (`PrefabOpHandlers.cs`) are within the ~10% tolerance
and can be deferred.

**Rationale**: Files over 500 LOC slow navigation and code review.
The handler split follows the existing `SceneOpTool.cs` +
`SceneOpHandlers.cs` pattern already used in the codebase.

**Effort**: Medium (extract methods, update any internal references)
**Risk**: Low (pure file reorganization, no logic change)

---

### 3. Group Director/ into subsystem subfolders

**Rule**: domain-grouped-tools
**Current**: `Editor/Tools/Director/` has 27 files in a flat directory.
**Proposed**: Group into subsystem subfolders matching
DIRECTOR-SURFACE.md grouping:

```
Editor/Tools/Director/
├── Shared/
│   ├── DirectorHelpers.cs
│   └── BatchTool.cs
├── Scenes/
│   ├── SceneOpTool.cs
│   └── SceneOpHandlers.cs
├── Prefabs/
│   ├── PrefabOpTool.cs
│   └── PrefabOpHandlers.cs
├── Assets/
│   ├── MaterialOpTool.cs
│   ├── ScriptableObjectOpTool.cs
│   ├── TextureOpTool.cs
│   ├── SpriteAtlasOpTool.cs
│   ├── PhysicsMaterialOpTool.cs
│   ├── AudioMixerOpTool.cs
│   ├── RenderPipelineOpTool.cs
│   └── AddressableOpTool.cs
├── Animation/
│   ├── AnimationClipOpTool.cs
│   ├── AnimatorControllerOpTool.cs
│   ├── BlendTreeOpTool.cs
│   └── TimelineOpTool.cs
├── Spatial/
│   ├── TerrainOpTool.cs
│   ├── TilemapOpTool.cs
│   ├── NavMeshOpTool.cs
│   └── ProBuilderOpTool.cs
├── Input/
│   └── InputActionOpTool.cs
└── Config/
    ├── ProjectSettingsOpTool.cs
    ├── QualityOpTool.cs
    ├── LightingOpTool.cs
    └── BuildProfileOpTool.cs
```

**Rationale**: 27 files exceeds the 15-file threshold. The subsystem
grouping mirrors DIRECTOR-SURFACE.md's tool groups (DirectorScene,
DirectorPrefab, DirectorAsset, DirectorAnim, DirectorSpatial,
DirectorInput, DirectorConfig), making navigation intuitive.

**Effort**: High (27 file moves + namespace updates + test using updates)
**Risk**: Medium (many files touched, but no logic change. Requires
updating `TheatreServer.RegisterBuiltInTools()` and all test `using`
statements.)

**Dependency**: Do after Step 1 (namespace fixes) and Step 2 (file splits)
to avoid double-touching files.

---

### 4. Move backlog docs from docs/ root to docs/designs/

**Rule**: docs-layout
**Current**: Two operational docs sit at `docs/` root alongside
foundation docs:
- `docs/structural-refactor-backlog.md`
- `docs/stylistic-refactor-backlog.md`

**Proposed**: Move both to `docs/designs/`:
- `docs/designs/structural-refactor-backlog.md`
- `docs/designs/stylistic-refactor-backlog.md`

**Rationale**: The docs-layout rule reserves `docs/` root for evergreen
foundation docs (ARCHITECTURE, CONTRACTS, STAGE-SURFACE, etc.). Backlogs
are operational/planning docs that belong in `docs/designs/`.

**Effort**: Trivial (git mv)
**Risk**: None

---

## Worth Considering

### 5. Split oversized non-Director files (>500 LOC)

**Rule**: file-size-cap
**Current**: 3 non-Director files exceed 500 lines:

| File | Lines | Notes |
|------|-------|-------|
| `Editor/UI/TheatreWindow.cs` | 789 | Editor window with multiple panels |
| `Runtime/Stage/Recording/RecordingDb.cs` | 739 | SQLite schema + queries |
| `Editor/Tools/Recording/RecordingTool.cs` | 711 | Recording tool handler |

**Proposed**: Split along natural boundaries:
- `TheatreWindow.cs` → extract panel-specific code into partial classes
  or separate files (e.g., `TheatreWindowToolsPanel.cs`)
- `RecordingDb.cs` → extract schema definitions or query builders
- `RecordingTool.cs` → extract into dispatcher + handlers (like SceneOp)

**Rationale**: These files exceed the cap, but they are less frequently
edited than Director tools. TheatreWindow is the most pressing since
UI code benefits most from smaller files.

**Effort**: Medium
**Risk**: Low

---

### 6. Move Stage root utilities to Shared/ subfolder

**Rule**: shared-utility-subfolder
**Current**: Two cross-cutting utilities sit at `Runtime/Stage/` root:
- `Runtime/Stage/JsonParamParser.cs`
- `Runtime/Stage/ResponseHelpers.cs`

**Proposed**: Move to `Runtime/Stage/Shared/`:
- `Runtime/Stage/Shared/JsonParamParser.cs`
- `Runtime/Stage/Shared/ResponseHelpers.cs`

Update namespace to `Theatre.Stage.Shared` (or keep `Theatre.Stage`
per the exception for widely-imported utilities).

**Rationale**: These are shared utilities consumed by GameObject/,
Spatial/, Recording/, and Director tools. Moving them to Shared/
clarifies they are cross-cutting, not Stage-specific features.

**Trade-off**: Every file that `using Theatre.Stage;` accesses
`ResponseHelpers` or `JsonParamParser` would need no change if the
namespace stays `Theatre.Stage`. The benefit is purely navigational.

**Effort**: Low (file move, maybe namespace update)
**Risk**: Low — but high churn if namespace changes (30+ consuming files)

---

### 7. Consolidate duplicate refactor plan files

**Rule**: docs-layout
**Current**: Three refactor plan files in `docs/designs/`:
- `REFACTOR-PLAN.md` (uppercase, possibly stale)
- `refactor-plan.md` (original plan, mostly executed)
- `refactor-plan-editor-tools.md` (current plan)

**Proposed**: Archive or delete stale plans. Keep
`refactor-plan-editor-tools.md` as the active plan. If the originals
have historical value, rename them with a `done-` prefix.

**Effort**: Trivial
**Risk**: None

---

## Not Worth It

### 8. Move 1:1 transport test files into feature groups

**Rule**: feature-grouped-tests
**Finding**: 5 test files in `Tests/Editor/` map 1:1 to source files:
`HttpTransportTests`, `JsonRpcTests`, `McpTypesTests`,
`RequestRouterTests`, `ToolRegistryTests`.

**Why not**: These test low-level infrastructure (HTTP transport, JSON-RPC
protocol, MCP types, request routing, tool registry). Each is a distinct
subsystem with its own contract. Feature-grouping them (e.g., "McpTests")
would produce a single 800+ line file with unrelated test fixtures. The
1:1 mapping is appropriate here — infrastructure tests test interfaces,
not user-facing features.

---

### 9. Reorganize Runtime/Director/ placeholders

**Rule**: organic-director-growth, keep-placeholders
**Finding**: All 7 `Runtime/Director/` subdirectories are empty
placeholders (.gitkeep only). Director tool implementations live in
`Editor/Tools/Director/` instead.

**Why not**: The Runtime/Director/ placeholders document the roadmap
per keep-placeholders rule. The actual Director tools live in Editor/
because they use `UnityEditor` APIs (AssetDatabase, Undo, etc.) which
are editor-only. The Runtime/ placeholders may eventually hold shared
types or runtime-safe Director abstractions, but forcing code into them
now would violate the organic-director-growth rule.

---

### 10. Create Shared/ subfolder in Editor/Tools/ for CompoundToolDispatcher

**Rule**: shared-utility-subfolder
**Finding**: `CompoundToolDispatcher.cs` sits at `Editor/Tools/` root
and is used by Director/, Scene/, Spatial/, Actions/, ECS/, Watch/,
and Recording/ tools.

**Why not**: There are only 6 files at the `Editor/Tools/` root, well
under the 15-file threshold. Creating a `Shared/` subfolder for a single
utility file (the rule says "wait until 2+ utilities") adds unnecessary
nesting. `CompoundToolDispatcher` is discoverable at the root.

---

## Implementation Order

1. **Step 4** — Move backlog docs (trivial, no code change)
2. **Step 7** — Consolidate refactor plans (trivial, no code change)
3. **Step 1** — Fix namespace misalignment (prerequisite for Step 3)
4. **Step 2** — Split oversized Director files (prerequisite for Step 3)
5. **Step 3** — Group Director/ into subfolders (depends on 1 and 2)
6. **Step 5** — Split non-Director oversized files (independent)
7. **Step 6** — Move Stage utilities to Shared/ (independent, low priority)
