# Rule: Feature-Grouped Tests

> Test files cover feature areas, not 1:1 with source files.

## Motivation

Theatre's tools are cohesive features where multiple classes collaborate (e.g., HierarchyWalker
+ ObjectResolver + ResponseHelpers form the scene awareness feature). Testing at the feature
level produces more realistic test scenarios and fewer brittle unit tests. It also avoids
test file proliferation — 52 source files don't need 52 test files.

## Before / After

### From this codebase: Scene awareness tests

**Current (keep as-is):**
```
Tests/Editor/
├── SceneAwarenessTests.cs      — covers HierarchyWalker, ObjectResolver, ResponseHelpers
├── WatchActionTests.cs         — covers WatchEngine, WatchConditions, ActionTool
├── SpatialQueryTests.cs        — covers SpatialIndex, all SpatialQuery* handlers
├── McpIntegrationTests.cs      — covers McpRouter, HttpTransport, RequestRouter
└── SceneToolIntegrationTests.cs — covers SceneHierarchy/Snapshot/Inspect/Delta tools
```

### Synthetic example: 1:1 test mapping (avoid this)

**Anti-pattern:**
```
Tests/
├── HierarchyWalkerTests.cs     — 3 tests
├── ObjectResolverTests.cs       — 4 tests
├── ResponseHelpersTests.cs      — 2 tests
├── WatchEngineTests.cs          — 3 tests
├── WatchConditionsTests.cs      — 5 tests
└── ... (50 more tiny test files)
```

**Preferred:**
```
Tests/
├── SceneAwarenessTests.cs       — 9 tests covering the feature
├── WatchTests.cs                — 8 tests covering watches end-to-end
└── ...
```

## Exceptions

- A utility class with complex logic (e.g., JsonParamParser, TokenBudget) may warrant its
  own test file if the tests don't fit naturally into a feature group
- If a feature test file exceeds 500 lines, split it into sub-feature test files
- New standalone subsystems (e.g., Director/) should start their own feature test files

## Scope

- Applies to: Tests/Editor/ (EditMode tests)
- Does NOT apply to: PlayMode tests (Tests/Runtime/) which may need different grouping
