# Rule: Namespace-Folder Alignment

> Namespace must mirror folder path exactly.

## Motivation

When namespaces match folder paths, developers can predict where a class lives from its
`using` statement and vice versa. This is the .NET Framework Design Guideline and Unity's
own convention. Misalignment causes confusion: "Where is `Theatre.Editor.ActionSetProperty`?
Is it in Editor/? Editor/Tools/? Editor/Tools/Actions/?"

## Before / After

### From this codebase: Actions/ namespace mismatch

**Before:**
```csharp
// File: Editor/Tools/Actions/ActionSetProperty.cs
namespace Theatre.Editor  // <-- doesn't reflect the Actions/ subfolder
{
    public static class ActionSetProperty { ... }
}
```

**After:**
```csharp
// File: Editor/Tools/Actions/ActionSetProperty.cs
namespace Theatre.Editor.Tools.Actions  // <-- mirrors folder path
{
    public static class ActionSetProperty { ... }
}
```

### From this codebase: SpatialQuery files

**Before:**
```csharp
// File: Editor/Tools/SpatialQueryRaycast.cs
namespace Theatre.Editor  // <-- all tools in one flat namespace
```

**After (with domain-grouped-tools rule applied):**
```csharp
// File: Editor/Tools/Spatial/SpatialQueryRaycast.cs
namespace Theatre.Editor.Tools.Spatial
```

### Synthetic example: Utility buried in wrong namespace

**Before:**
```csharp
// File: Runtime/Stage/Shared/JsonParamParser.cs
namespace Theatre.Stage  // <-- doesn't include Shared/
```

**After:**
```csharp
// File: Runtime/Stage/Shared/JsonParamParser.cs
namespace Theatre.Stage.Shared
```

## Exceptions

- The package root assembly namespaces (`Theatre`, `Theatre.Editor`, `Theatre.Tests.Editor`)
  are set by asmdef `rootNamespace` and apply to files directly in those folders
- If a subfolder contains only 1-2 internal helpers that are never imported externally,
  keeping the parent namespace is acceptable to avoid import noise
- Generated files (e.g., Unity-generated .cs) may not follow this rule

## Scope

- Applies to: All .cs files in Runtime/ and Editor/
- Does NOT apply to: Tests (Theatre.Tests.Editor is flat by convention)
