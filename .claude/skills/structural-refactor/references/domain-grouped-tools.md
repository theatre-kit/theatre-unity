# Rule: Domain-Grouped Tools

> Group Editor/Tools/ into domain subfolders when the directory exceeds ~15 files.

## Motivation

A flat directory with 20+ files forces developers to scan file names to find related tools.
Domain subfolders let you navigate by concept (Scene, Spatial, Watch) rather than alphabetical
scanning. This matters most for onboarding — new contributors find the right file faster.

## Before / After

### From this codebase: Editor/Tools/ flat layout

**Before:**
```
Editor/Tools/
├── ActionTool.cs
├── Actions/
│   ├── ActionSetProperty.cs
│   ├── ActionInvokeMethod.cs
│   ├── ActionPlayControl.cs
│   ├── ActionSetActive.cs
│   ├── ActionTeleport.cs
│   └── ActionSetTimescale.cs
├── ConsoleLogBuffer.cs
├── PropertySerializer.cs
├── SceneDeltaTool.cs
├── SceneHierarchyTool.cs
├── SceneInspectTool.cs
├── SceneSnapshotTool.cs
├── SpatialQueryBounds.cs
├── SpatialQueryLinecast.cs
├── SpatialQueryNearest.cs
├── SpatialQueryOverlap.cs
├── SpatialQueryPathDistance.cs
├── SpatialQueryRadius.cs
├── SpatialQueryRaycast.cs
├── SpatialQueryTool.cs
├── SpatialResultBuilder.cs
├── TestSceneCreator.cs
├── TheatreStatusTool.cs
├── UnityConsoleTool.cs
├── UnityTestsTool.cs
└── WatchTool.cs
```

**After:**
```
Editor/Tools/
├── Actions/
│   ├── ActionTool.cs            (dispatcher)
│   ├── ActionSetProperty.cs
│   ├── ActionInvokeMethod.cs
│   ├── ActionPlayControl.cs
│   ├── ActionSetActive.cs
│   ├── ActionTeleport.cs
│   └── ActionSetTimescale.cs
├── Scene/
│   ├── SceneHierarchyTool.cs
│   ├── SceneSnapshotTool.cs
│   ├── SceneInspectTool.cs
│   ├── SceneDeltaTool.cs
│   └── PropertySerializer.cs
├── Spatial/
│   ├── SpatialQueryTool.cs      (dispatcher)
│   ├── SpatialQueryBounds.cs
│   ├── SpatialQueryLinecast.cs
│   ├── SpatialQueryNearest.cs
│   ├── SpatialQueryOverlap.cs
│   ├── SpatialQueryPathDistance.cs
│   ├── SpatialQueryRadius.cs
│   ├── SpatialQueryRaycast.cs
│   └── SpatialResultBuilder.cs
├── Watch/
│   └── WatchTool.cs
├── ConsoleLogBuffer.cs
├── TestSceneCreator.cs
├── TheatreStatusTool.cs
├── UnityConsoleTool.cs
└── UnityTestsTool.cs
```

### Synthetic example: Plugin system with mixed tools

**Before:**
```
Plugins/
├── AudioAnalyzer.cs
├── AudioMixer.cs
├── ImageFilter.cs
├── ImageResize.cs
├── TextFormatter.cs
├── TextParser.cs
└── ... (18 more files)
```

**After:**
```
Plugins/
├── Audio/
│   ├── AudioAnalyzer.cs
│   └── AudioMixer.cs
├── Image/
│   ├── ImageFilter.cs
│   └── ImageResize.cs
└── Text/
    ├── TextFormatter.cs
    └── TextParser.cs
```

## Exceptions

- Directories with fewer than ~15 files do not need subfolders — naming prefixes suffice
- Singleton tools that don't belong to any domain group (e.g., TheatreStatusTool) stay at the Tools/ root
- Don't create a subfolder for a single file unless it's expected to grow

## Scope

- Applies to: Editor/Tools/, and any future directory that grows past ~15 files
- Does NOT apply to: Runtime/ (already well-organized), Tests/ (grouped by feature)
