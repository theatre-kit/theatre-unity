# Rule: Shared Utility Subfolder

> Cross-cutting utilities live in dedicated Shared/ subfolders, not at the domain root.

## Motivation

When utilities sit at the domain root alongside feature files, it's unclear whether they're
a feature or a helper. A `Shared/` subfolder signals "this code is used by multiple siblings"
and prevents the root from becoming a dumping ground as the codebase grows.

## Before / After

### From this codebase: Runtime/Stage/ utilities at root

**Before:**
```
Runtime/Stage/
├── GameObject/
│   ├── HierarchyWalker.cs
│   ├── ObjectResolver.cs
│   └── ...
├── Spatial/
│   ├── SpatialIndex.cs
│   └── ...
├── JsonParamParser.cs       <-- utility at domain root
└── ResponseHelpers.cs       <-- utility at domain root
```

**After:**
```
Runtime/Stage/
├── GameObject/
│   └── ...
├── Spatial/
│   └── ...
└── Shared/
    ├── JsonParamParser.cs
    └── ResponseHelpers.cs
```

### From this codebase: Editor/Tools/ utility

**Before:**
```
Editor/Tools/
├── SpatialResultBuilder.cs  <-- utility mixed with tools
├── SpatialQueryTool.cs
└── ...
```

**After:**
```
Editor/Tools/
├── Shared/
│   └── SpatialResultBuilder.cs
├── Spatial/
│   └── SpatialQueryTool.cs
└── ...
```

### Synthetic example: Helpers scattered across features

**Before:**
```
Services/
├── Auth/
│   └── AuthService.cs
├── Billing/
│   └── BillingService.cs
├── DateHelper.cs            <-- used by Auth and Billing
└── ValidationHelper.cs      <-- used by Auth and Billing
```

**After:**
```
Services/
├── Auth/
│   └── AuthService.cs
├── Billing/
│   └── BillingService.cs
└── Shared/
    ├── DateHelper.cs
    └── ValidationHelper.cs
```

## Exceptions

- If a utility is only used by one sibling subfolder, place it in that subfolder instead
- Don't create Shared/ for a single file — wait until there are 2+ utilities
- Runtime/Core/ files (TheatreConfig, ToolRegistry) are not "utilities" — they're core types

## Scope

- Applies to: Runtime/Stage/, Editor/Tools/, and any future domain directory
- Does NOT apply to: Runtime/Core/ (bootstrapping, not shared utilities),
  Runtime/Transport/ (cohesive subsystem, no shared helpers)
