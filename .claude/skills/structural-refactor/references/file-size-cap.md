# Rule: File Size Cap

> Split files exceeding 500 lines of code.

## Motivation

Files over 500 LOC become hard to navigate and reason about. They often indicate a class
doing too much. Unity's compile-per-assembly model means large files don't slow builds,
but they slow humans. The 500-line cap gives room for complex tools (current max is 392 LOC)
while catching genuine bloat.

## Before / After

### Synthetic example: Oversized tool handler

**Before (620 lines):**
```csharp
// SceneExportTool.cs — 620 lines
public static class SceneExportTool
{
    // Lines 1-200: Parameter parsing and validation
    // Lines 200-400: Scene traversal and data collection
    // Lines 400-620: Format conversion and serialization
}
```

**After:**
```csharp
// SceneExportTool.cs — 180 lines (orchestrator)
public static class SceneExportTool
{
    public static string Execute(JObject args) { ... }
}

// SceneExportCollector.cs — 200 lines (data collection)
internal static class SceneExportCollector
{
    public static ExportData Collect(ExportParams p) { ... }
}

// SceneExportSerializer.cs — 220 lines (format conversion)
internal static class SceneExportSerializer
{
    public static string Serialize(ExportData data, string format) { ... }
}
```

### From this codebase: Current healthy example

**SceneDeltaTool.cs (392 LOC) — within limits:**
```
Editor/Tools/SceneDeltaTool.cs  — 392 lines, single responsibility (state diff)
```
This file is fine as-is. No split needed.

## Exceptions

- Test files may exceed 500 LOC if they cover a broad feature area — splitting tests
  across files can break test fixture setup sharing
- Auto-generated files are exempt
- A file at 510 lines with cohesive logic is better than a forced split into two awkward
  files — use judgment within ~10% of the cap

## Scope

- Applies to: All .cs source files in Runtime/ and Editor/
- Does NOT apply to: Design docs, generated code, test fixtures (soft limit only)
