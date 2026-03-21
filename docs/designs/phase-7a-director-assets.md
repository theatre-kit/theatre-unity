# Design: Phase 7a — Director: Core Asset Tools

## Overview

Three asset creation/modification tools: `material_op` for Materials,
`scriptable_object_op` for ScriptableObjects, and `physics_material_op`
for PhysicMaterials. All use the Phase 6 Director infrastructure
(`DirectorHelpers`, undo groups, asset path validation, dry run).

All tools live in `Editor/Tools/Director/` and register under
`ToolGroup.DirectorAsset`.

---

## Architecture

```
Editor/Tools/Director/
  MaterialOpTool.cs         — MCP compound tool: material_op (4 operations)
  ScriptableObjectOpTool.cs — MCP compound tool: scriptable_object_op (4 operations)
  PhysicsMaterialOpTool.cs  — MCP compound tool: physics_material_op (2 operations)
```

Three new files, all following the established compound tool pattern.
All use existing `DirectorHelpers` for shared utilities.

---

## Implementation Units

### Unit 1: MaterialOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/MaterialOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class MaterialOpTool
{
    private static readonly JToken s_inputSchema;
    static MaterialOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    // Operations (internal for test access)
    internal static string Create(JObject args);
    internal static string SetProperties(JObject args);
    internal static string SetShader(JObject args);
    internal static string ListProperties(JObject args);
}
```

**Registration**: group `ToolGroup.DirectorAsset`, `ReadOnlyHint = false`.

**Operations**:

#### `create`
- Required: `asset_path` (must end in `.mat`), `shader` (shader name string)
- Optional: `properties` (JObject of shader property name → value)
- Optional: `dry_run`
- Find shader: `Shader.Find(shaderName)` — error `"shader_not_found"` if null
- Create: `new Material(shader)`
- If `properties`: set each via `Material.SetColor/SetFloat/SetTexture/SetVector`
  based on property type (infer from value type in JSON)
- Save: `AssetDatabase.CreateAsset(material, assetPath)`
- `Undo.RegisterCreatedObjectUndo(material, "Theatre material_op:create")`
- Response: `{ "result": "ok", "operation": "create", "asset_path": "...", "shader": "..." }`

**Property type inference from JSON**:
| JSON value | Material method |
|-----------|----------------|
| Number (float) | `SetFloat(name, value)` |
| Integer | `SetInt(name, value)` |
| Array of 4 numbers | `SetColor(name, new Color(r,g,b,a))` |
| Array of 2-3 numbers | `SetVector(name, new Vector4(x,y,z,w))` |
| String (starts with "Assets/") | `SetTexture(name, AssetDatabase.LoadAssetAtPath<Texture>(path))` |
| Boolean (true/false) | `SetFloat(name, value ? 1f : 0f)` (shader keywords often use 0/1) |

#### `set_properties`
- Required: `asset_path`, `properties` (JObject)
- Load material: `AssetDatabase.LoadAssetAtPath<Material>(assetPath)`
- Error if not found: `"asset_not_found"`
- `Undo.RecordObject(material, "Theatre material_op:set_properties")`
- Set each property using the same type inference
- `EditorUtility.SetDirty(material)`
- Response: `{ "result": "ok", "operation": "set_properties", "asset_path": "...", "properties_set": N }`

#### `set_shader`
- Required: `asset_path`, `shader`
- Load material, find shader
- `Undo.RecordObject(material, "Theatre material_op:set_shader")`
- `material.shader = shader`
- `EditorUtility.SetDirty(material)`
- Response with old and new shader names

#### `list_properties`
- Required: `asset_path`
- Load material
- Iterate `material.shader.GetPropertyCount()`:
  - `shader.GetPropertyName(i)`, `shader.GetPropertyType(i)`
  - Read current value based on type
- Response: `{ "result": "ok", "operation": "list_properties", "asset_path": "...", "shader": "...", "properties": [...] }`
- Each property: `{ "name": "_BaseColor", "type": "color", "value": [1,0,0,1] }`
- This is read-only — no undo needed

**Property type mapping for `list_properties`**:
| `ShaderPropertyType` | JSON type | Read method |
|---------------------|-----------|-------------|
| `Color` | `[r,g,b,a]` | `material.GetColor(name)` |
| `Float` / `Range` | `number` | `material.GetFloat(name)` |
| `Vector` | `[x,y,z,w]` | `material.GetVector(name)` |
| `Texture` | `string` (asset path) or `null` | `material.GetTexture(name)` → `AssetDatabase.GetAssetPath` |
| `Int` | `integer` | `material.GetInt(name)` |

**JSON Schema** properties:
- `operation` (required, enum: create, set_properties, set_shader, list_properties)
- `asset_path` (string)
- `shader` (string — shader name, e.g. "Universal Render Pipeline/Lit")
- `properties` (object — property name → value pairs)
- `dry_run` (boolean)

**Acceptance Criteria**:
- [ ] `create` with shader and properties produces a .mat file
- [ ] `set_properties` modifies an existing material
- [ ] `set_shader` changes the shader
- [ ] `list_properties` returns all shader properties with types and values
- [ ] Unknown shader returns `shader_not_found` error
- [ ] All mutating operations register Undo
- [ ] `dry_run` validates without creating

---

### Unit 2: ScriptableObjectOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/ScriptableObjectOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class ScriptableObjectOpTool
{
    private static readonly JToken s_inputSchema;
    static ScriptableObjectOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string SetFields(JObject args);
    internal static string ListFields(JObject args);
    internal static string FindByType(JObject args);
}
```

**Registration**: group `ToolGroup.DirectorAsset`, `ReadOnlyHint = false`.

**Operations**:

#### `create`
- Required: `type` (ScriptableObject type name), `asset_path` (must end in `.asset`)
- Optional: `fields` (JObject of field name → value)
- Resolve type: search assemblies for a type that inherits from `ScriptableObject`
  and has `type.Name == typeName`. Use a similar approach to
  `DirectorHelpers.ResolveComponentType` but filter on `ScriptableObject` instead
  of `Component`.
- Create: `ScriptableObject.CreateInstance(type)`
- If `fields`: set via `SerializedObject`/`SerializedProperty` using
  `DirectorHelpers.SetProperties` pattern (the SO is a `UnityEngine.Object`,
  so `new SerializedObject(instance)` works)
- Save: `AssetDatabase.CreateAsset(instance, assetPath)`
- `Undo.RegisterCreatedObjectUndo(instance, "Theatre scriptable_object_op:create")`
- Response: `{ "result": "ok", "operation": "create", "asset_path": "...", "type": "..." }`

**Type resolution note**: Add a helper `ResolveScriptableObjectType(string typeName, out string error)` to `DirectorHelpers` (or as a local helper in this file). Same pattern as `ResolveComponentType` but checking `typeof(ScriptableObject).IsAssignableFrom(type)`.

#### `set_fields`
- Required: `asset_path`, `fields` (JObject)
- Load: `AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath)`
- Error if not found: `"asset_not_found"`
- `Undo.RecordObject(so, "Theatre scriptable_object_op:set_fields")`
- Set fields via `SerializedObject`/`SerializedProperty` using the
  same 4-step name fallback as `DirectorHelpers.SetProperties`
- `EditorUtility.SetDirty(so)`
- Response with fields_set count

#### `list_fields`
- Required: `asset_path`
- Load SO, create `SerializedObject`
- Iterate properties via `GetIterator()` / `NextVisible(true)`
- Skip `m_Script` (internal Unity field)
- For each: name (snake_case), type, current value
- Response: `{ "result": "ok", "operation": "list_fields", "asset_path": "...", "type": "...", "fields": [...] }`
- Read-only — no undo

#### `find_by_type`
- Required: `type` (SO type name)
- Use `AssetDatabase.FindAssets($"t:{typeName}")`
- For each GUID: `AssetDatabase.GUIDToAssetPath(guid)`
- Response: `{ "result": "ok", "operation": "find_by_type", "type": "...", "assets": [{"asset_path": "...", "name": "..."}] }`
- Read-only

**JSON Schema** properties:
- `operation` (required, enum: create, set_fields, list_fields, find_by_type)
- `type` (string — SO type name)
- `asset_path` (string)
- `fields` (object — field name → value pairs)
- `dry_run` (boolean)

**Acceptance Criteria**:
- [ ] `create` produces a .asset file with the correct type
- [ ] `set_fields` modifies serialized fields on an existing SO
- [ ] `list_fields` returns all visible serialized fields
- [ ] `find_by_type` finds all assets of a given SO type
- [ ] Unknown SO type returns `type_not_found` error
- [ ] All mutating operations register Undo

---

### Unit 3: PhysicsMaterialOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/PhysicsMaterialOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class PhysicsMaterialOpTool
{
    private static readonly JToken s_inputSchema;
    static PhysicsMaterialOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string SetProperties(JObject args);
}
```

**Registration**: group `ToolGroup.DirectorAsset`, `ReadOnlyHint = false`.

**Operations**:

#### `create`
- Required: `asset_path` (must end in `.physicMaterial` or `.physicsMaterial2D`)
- Optional: `physics` ("3d" or "2d", default "3d")
- Optional: `friction`, `bounciness`, `friction_combine`, `bounce_combine`
- For 3D: `new PhysicMaterial()`, set `dynamicFriction`, `staticFriction`,
  `bounciness`, `frictionCombine`, `bounceCombine`
- For 2D: `new PhysicsMaterial2D()`, set `friction`, `bounciness`
- Save: `AssetDatabase.CreateAsset(material, assetPath)`
- `Undo.RegisterCreatedObjectUndo(material, "...")`
- Response with asset_path and physics mode

**Note on asset extension**: Unity 6 may use `.physicMaterial` for 3D and
`.physicsMaterial2D` for 2D. Check what `AssetDatabase.CreateAsset` expects.
Actually, both can use `.asset` extension. Use whatever the user provides
as long as it validates. Alternatively, default to `.asset` if no extension.

#### `set_properties`
- Required: `asset_path`
- Optional: `friction`, `bounciness`, `friction_combine`, `bounce_combine`,
  `static_friction` (3D only)
- Load asset, detect if 3D or 2D by checking type
- `Undo.RecordObject(material, "...")`
- Set the provided properties
- `EditorUtility.SetDirty(material)`
- Response with updated values

**Combine mode mapping**: `"average"`, `"minimum"`, `"maximum"`, `"multiply"`
→ `PhysicMaterialCombine.Average` etc.

**JSON Schema** properties:
- `operation` (required, enum: create, set_properties)
- `asset_path` (string)
- `physics` (string, "3d" or "2d")
- `friction`, `static_friction`, `bounciness` (number)
- `friction_combine`, `bounce_combine` (string enum)
- `dry_run` (boolean)

**Acceptance Criteria**:
- [ ] `create` with 3D produces a PhysicMaterial asset
- [ ] `create` with 2D produces a PhysicsMaterial2D asset
- [ ] `set_properties` modifies friction and bounciness
- [ ] Combine modes map correctly
- [ ] All mutations register Undo

---

### Unit 4: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add to `RegisterBuiltInTools()`:
```csharp
MaterialOpTool.Register(registry);           // Phase 7a
ScriptableObjectOpTool.Register(registry);   // Phase 7a
PhysicsMaterialOpTool.Register(registry);    // Phase 7a
```

**Acceptance Criteria**:
- [ ] All 3 tools appear in `tools/list` when DirectorAsset is enabled
- [ ] Tools hidden when DirectorAsset is disabled

---

### Unit 5: DirectorHelpers Extension — SO Type Resolution

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/DirectorHelpers.cs` (modify)

Add:
```csharp
/// <summary>
/// Resolve a ScriptableObject type by name. Same logic as
/// ResolveComponentType but filters on ScriptableObject inheritance.
/// </summary>
public static Type ResolveScriptableObjectType(
    string typeName, out string error);
```

**Implementation**: Copy `ResolveComponentType` logic but change:
- `typeof(Component).IsAssignableFrom(type)` → `typeof(ScriptableObject).IsAssignableFrom(type)`
- Error messages: "ScriptableObject type" instead of "Component type"

Also add a helper for setting fields on any `UnityEngine.Object` via SerializedObject:

```csharp
/// <summary>
/// Set multiple fields on any UnityEngine.Object via SerializedObject.
/// Works for Components, ScriptableObjects, or any serialized asset.
/// </summary>
public static (int successCount, List<string> errors)
    SetFields(UnityEngine.Object target, JObject fields);
```

This is essentially the same as `SetProperties` (which takes a `Component`)
but generalized to `UnityEngine.Object`. Refactor `SetProperties` to call
`SetFields` internally, or just make `SetFields` the one implementation and
have `SetProperties` delegate to it.

**Acceptance Criteria**:
- [ ] `ResolveScriptableObjectType("ScriptableObject")` returns the base type
- [ ] `ResolveScriptableObjectType("NonExistent")` returns null with error
- [ ] `SetFields` works on both Components and ScriptableObjects

---

## Implementation Order

```
Unit 5: DirectorHelpers extension (SO type resolution + SetFields)
  └─ Unit 1: MaterialOpTool
  └─ Unit 2: ScriptableObjectOpTool (depends on SO type resolution)
  └─ Unit 3: PhysicsMaterialOpTool
     └─ Unit 4: Server Integration
```

Units 1-3 are independent after Unit 5 (helpers). Unit 4 is last.

---

## Testing

### Tests: `Tests/Editor/AssetToolTests.cs`

```csharp
[TestFixture]
public class MaterialOpTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = "Assets/_TheatreTest_Materials";
        if (!AssetDatabase.IsValidFolder(_tempDir))
            AssetDatabase.CreateFolder("Assets", "_TheatreTest_Materials");
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(_tempDir);
    }

    [Test] public void Create_WithShader_CreatesMatFile() { }
    [Test] public void Create_UnknownShader_ReturnsError() { }
    [Test] public void SetProperties_ModifiesMaterial() { }
    [Test] public void SetShader_ChangesShader() { }
    [Test] public void ListProperties_ReturnsShaderProps() { }
    [Test] public void DryRun_DoesNotCreateFile() { }
}

[TestFixture]
public class ScriptableObjectOpTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = "Assets/_TheatreTest_SO";
        if (!AssetDatabase.IsValidFolder(_tempDir))
            AssetDatabase.CreateFolder("Assets", "_TheatreTest_SO");
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(_tempDir);
    }

    [Test] public void Create_CreatesAssetFile() { }
    [Test] public void Create_UnknownType_ReturnsError() { }
    [Test] public void FindByType_FindsAssets() { }
    [Test] public void ListFields_ReturnsFields() { }
}

[TestFixture]
public class PhysicsMaterialOpTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = "Assets/_TheatreTest_PhysMat";
        if (!AssetDatabase.IsValidFolder(_tempDir))
            AssetDatabase.CreateFolder("Assets", "_TheatreTest_PhysMat");
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(_tempDir);
    }

    [Test] public void Create3D_CreatesPhysicMaterial() { }
    [Test] public void Create2D_CreatesPhysicsMaterial2D() { }
    [Test] public void SetProperties_ModifiesFriction() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: call `material_op` `create` → verify .mat file in Assets
5. Manual: call `scriptable_object_op` `create` → verify .asset file
6. Manual: call `physics_material_op` `create` → verify physics material
7. Ctrl+Z → verify undo works for all asset creations
