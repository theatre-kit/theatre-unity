# Design: Phase 9b — Director: Terrain & ProBuilder

## Overview

Two spatial world-building tools: `terrain_op` for terrain creation and
sculpting (heightmaps, textures, trees, details), and `probuilder_op`
for mesh creation and editing via ProBuilder.

`terrain_op` uses built-in Unity APIs (`UnityEngine.TerrainData`, `Terrain`).
`probuilder_op` requires `com.unity.probuilder` (now installed) — guarded
by `#if THEATRE_HAS_PROBUILDER` via `versionDefines`.

All tools live in `Editor/Tools/Director/` under `ToolGroup.DirectorSpatial`.

---

## Architecture

```
Editor/Tools/Director/
  TerrainOpTool.cs       — MCP tool: terrain_op (9 operations)
  ProBuilderOpTool.cs    — MCP tool: probuilder_op (6 operations, #if guarded)
```

---

## Implementation Units

### Unit 1: Asmdef — ProBuilder Version Define

**File**: `Packages/com.theatre.toolkit/Editor/com.theatre.toolkit.editor.asmdef` (modify)

Add to `versionDefines`:
```json
{
    "name": "com.unity.probuilder",
    "expression": "",
    "define": "THEATRE_HAS_PROBUILDER"
}
```

Add to `references`: `"Unity.ProBuilder"`, `"Unity.ProBuilder.Editor"`

Also add `com.unity.ai.navigation` versionDefine + references (now installed):
```json
{
    "name": "com.unity.ai.navigation",
    "expression": "",
    "define": "THEATRE_HAS_AI_NAVIGATION"
}
```
Add to `references`: `"Unity.AI.Navigation"`, `"Unity.AI.Navigation.Editor"`

**Test asmdef** (`Tests/Editor/com.theatre.toolkit.editor.tests.asmdef`):
Same additions to `versionDefines` and `references`.

**Acceptance Criteria**:
- [ ] `THEATRE_HAS_PROBUILDER` defined when ProBuilder installed
- [ ] `THEATRE_HAS_AI_NAVIGATION` defined when AI Navigation installed
- [ ] Compilation succeeds when packages are absent

---

### Unit 2: TerrainOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/TerrainOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class TerrainOpTool
{
    private static readonly JToken s_inputSchema;
    static TerrainOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string SetHeightmap(JObject args);
    internal static string SmoothHeightmap(JObject args);
    internal static string PaintTexture(JObject args);
    internal static string AddTerrainLayer(JObject args);
    internal static string PlaceTrees(JObject args);
    internal static string PlaceDetails(JObject args);
    internal static string SetSize(JObject args);
    internal static string GetHeight(JObject args);
}
```

**Registration**: name `"terrain_op"`, group `ToolGroup.DirectorSpatial`.

#### `create`
- Required: `asset_path` (for TerrainData, must end in `.asset`)
- Optional: `width` (float, default 1000), `height` (float, default 600),
  `length` (float, default 1000), `heightmap_resolution` (int, default 513,
  must be 2^n+1), `position` ([x,y,z])
- Create TerrainData: `new TerrainData()`, set `size = new Vector3(width, height, length)`,
  `heightmapResolution = resolution`
- `AssetDatabase.CreateAsset(terrainData, assetPath)`
- Create Terrain GameObject: `Terrain.CreateTerrainGameObject(terrainData)`
- If position: set transform.position
- `Undo.RegisterCreatedObjectUndo(go, "Theatre terrain_op:create")`
- Response with asset_path and terrain path

#### `set_heightmap`
- Required: `terrain_path` (hierarchy path to Terrain GO)
- Required: `heights` (2D array of floats, 0.0-1.0)
- Optional: `region` (`{ "x": int, "y": int }` — offset into heightmap, default 0,0)
- Resolve Terrain component
- `Undo.RecordObject(terrain.terrainData, "Theatre terrain_op:set_heightmap")`
- `terrain.terrainData.SetHeights(x, y, heights)`
- Response with dimensions set

**Heights format**: `heights` is a JArray of JArrays (rows). Convert to `float[,]`:
```csharp
var rows = heights as JArray;
int h = rows.Count;
int w = ((JArray)rows[0]).Count;
var data = new float[h, w];
for (int r = 0; r < h; r++)
    for (int c = 0; c < w; c++)
        data[r, c] = ((JArray)rows[r])[c].Value<float>();
terrain.terrainData.SetHeights(offsetX, offsetY, data);
```

#### `smooth_heightmap`
- Required: `terrain_path`
- Optional: `region` (`{ "x", "y", "width", "height" }` — default full),
  `iterations` (int, default 1)
- Read heights, apply averaging kernel, write back
- Simple 3x3 box blur per iteration
- Response

#### `paint_texture`
- Required: `terrain_path`, `layer_index` (int), `positions` (array of [x,z] in world space)
- Optional: `opacity` (float 0-1, default 1), `brush_size` (int in alphamap cells, default 5)
- Convert world positions to alphamap coordinates
- Modify `terrain.terrainData.GetAlphamaps` / `SetAlphamaps`
- Response with painted count

#### `add_terrain_layer`
- Required: `terrain_path`, `diffuse_texture` (asset path to Texture2D)
- Optional: `normal_texture` (asset path), `tile_size` ([x,y], default [15,15]),
  `tile_offset` ([x,y], default [0,0])
- Create `TerrainLayer`, set textures and tiling
- Add to terrain's layers array: `terrain.terrainData.terrainLayers`
- Response with layer index

#### `place_trees`
- Required: `terrain_path`, `prefab` (asset path to tree prefab),
  `positions` (array of [x,z] in world space)
- Optional: `height_scale` (float, default 1), `width_scale` (float, default 1),
  `rotation` (float in degrees, default random)
- Convert world positions to terrain-local normalized coords (0-1)
- Build `TreeInstance[]` and add via `terrain.terrainData.SetTreeInstances`
  or append to existing
- First ensure the tree prototype exists: add to `treePrototypes` if not present
- Response with count placed

#### `place_details`
- Required: `terrain_path`, `layer_index` (int),
  `positions` (array of [x,z] in world space)
- Optional: `density` (int, default 1)
- Convert to detail map coordinates
- Modify `terrain.terrainData.GetDetailLayer` / `SetDetailLayer`
- Response

#### `set_size`
- Required: `terrain_path`
- Optional: `width`, `height`, `length` (floats)
- `terrain.terrainData.size = new Vector3(...)`
- Response

#### `get_height` (READ-ONLY)
- Required: `terrain_path`, `position` ([x,z] world space)
- `terrain.SampleHeight(new Vector3(x, 0, z))`
- Response: `{ "result": "ok", "position": [x, height, z] }`

**JSON Schema** properties:
- `operation` (required, enum of 9 ops)
- `asset_path`, `terrain_path`, `position`, `positions`
- `width`, `height`, `length`, `heightmap_resolution`
- `heights`, `region`, `iterations`
- `layer_index`, `diffuse_texture`, `normal_texture`, `tile_size`, `tile_offset`
- `prefab`, `height_scale`, `width_scale`, `rotation`
- `opacity`, `brush_size`, `density`
- `dry_run`

**Acceptance Criteria**:
- [ ] `create` produces a TerrainData asset and Terrain GameObject
- [ ] `set_heightmap` modifies terrain heights
- [ ] `add_terrain_layer` adds a texture layer
- [ ] `get_height` samples correct terrain height
- [ ] `set_size` changes terrain dimensions

---

### Unit 3: ProBuilderOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/ProBuilderOpTool.cs`

**Entire file wrapped in `#if THEATRE_HAS_PROBUILDER`**.

```csharp
#if THEATRE_HAS_PROBUILDER
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEditor.ProBuilder;

namespace Theatre.Editor.Tools.Director
{
    public static class ProBuilderOpTool
    {
        private static readonly JToken s_inputSchema;
        static ProBuilderOpTool();
        public static void Register(ToolRegistry registry);
        private static string Execute(JToken arguments);

        internal static string CreateShape(JObject args);
        internal static string ExtrudeFaces(JObject args);
        internal static string SetMaterial(JObject args);
        internal static string Merge(JObject args);
        internal static string BooleanOp(JObject args);
        internal static string ExportMesh(JObject args);
    }
}
#endif
```

**Registration**: name `"probuilder_op"`, group `ToolGroup.DirectorSpatial`.

#### `create_shape`
- Required: `shape` (string: "cube"/"cylinder"/"sphere"/"plane"/"stair"/"arch"/"door"/"pipe"/"cone"/"torus"/"prism")
- Optional: `position` ([x,y,z]), `size` ([x,y,z] or single float for uniform),
  `name` (string)
- `var mesh = ShapeGenerator.CreateShape(shapeType)`
  Map shape strings to `ShapeType` enum
- Set position, name
- `Undo.RegisterCreatedObjectUndo(mesh.gameObject, "...")`
- Response with path and instance_id

#### `extrude_faces`
- Required: `path` (hierarchy path to ProBuilder mesh), `faces` (array of face indices)
- Optional: `distance` (float, default 1)
- Resolve GO, get `ProBuilderMesh` component
- Select faces by index: `mesh.faces[index]`
- `mesh.Extrude(selectedFaces, ExtrudeMethod.FaceNormal, distance)`
- `mesh.ToMesh()` + `mesh.Refresh()`
- Response

#### `set_material`
- Required: `path`, `material` (asset path to Material), `faces` (face indices array)
- Resolve mesh, load material
- For each face: `face.submeshIndex = ...` — actually ProBuilder uses
  `MeshRenderer` with sub-mesh materials. Set via:
  `mesh.SetMaterial(selectedFaces, material)`
- `mesh.ToMesh()` + `mesh.Refresh()`
- Response

#### `merge`
- Required: `paths` (array of hierarchy paths to ProBuilder meshes)
- Resolve all, verify all have `ProBuilderMesh`
- `CombineMeshes.Combine(meshes, meshes[0])`
- Response with merged path

#### `boolean_op`
- Required: `path_a`, `path_b`, `operation` ("union"/"subtract"/"intersect")
- ProBuilder CSG: `UnityEngine.ProBuilder.Csg.CSG_Operation`
  Check if CSG is available — it may be in a separate namespace
- If not available: return error suggesting manual approach
- Response

#### `export_mesh`
- Required: `path` (ProBuilder mesh), `asset_path` (where to save .asset)
- Get the `Mesh` from the `MeshFilter`
- `var meshCopy = Object.Instantiate(meshFilter.sharedMesh)`
- `AssetDatabase.CreateAsset(meshCopy, assetPath)`
- Response

**Acceptance Criteria**:
- [ ] `create_shape` creates a ProBuilder cube/cylinder/etc
- [ ] `extrude_faces` extrudes selected faces
- [ ] `set_material` assigns material to faces
- [ ] `merge` combines multiple ProBuilder meshes
- [ ] `export_mesh` saves mesh as .asset
- [ ] Tool hidden when ProBuilder not installed

---

### Unit 4: NavMeshOpTool Update — Use AI Navigation Package

**Phase Dependency**: This unit modifies Phase 9a's `NavMeshOpTool` to
add terrain integration. Phase 9b must be implemented after Phase 9a.
This is consistent with the ROADMAP's sequential phase ordering — Phase
9a (Tilemap & NavMesh) is a prerequisite for Phase 9b (Terrain &
ProBuilder).

If Phase 9a has not been implemented yet, skip this unit and add the
terrain-NavMesh integration later as a follow-up.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/NavMeshOpTool.cs` (modify)

Now that `com.unity.ai.navigation` is installed, update `add_modifier` and
`add_surface` to use direct API calls instead of reflection:

```csharp
#if THEATRE_HAS_AI_NAVIGATION
using Unity.AI.Navigation;
#endif
```

Replace the reflection-based `add_modifier` and `add_surface` with direct
`#if THEATRE_HAS_AI_NAVIGATION` guarded code. Keep the reflection fallback
inside `#else` for when the package isn't installed.

---

### Unit 5: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

After Phase 9a registrations, add:
```csharp
            TerrainOpTool.Register(registry);           // Phase 9b
#if THEATRE_HAS_PROBUILDER
            ProBuilderOpTool.Register(registry);        // Phase 9b
#endif
```

---

## Implementation Order

```
Unit 1: Asmdef updates (ProBuilder + AI Navigation versionDefines)
Unit 4: NavMeshOpTool update (use direct AI Navigation API)
Unit 2: TerrainOpTool (independent)
Unit 3: ProBuilderOpTool (independent)
Unit 5: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/TerrainProBuilderToolTests.cs`

```csharp
[TestFixture]
public class TerrainOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* Assets/_TheatreTest_Terrain */ }
    [TearDown] public void TearDown() { /* delete assets + destroy terrain GO */ }

    [Test] public void Create_ProducesTerrainAssetAndGameObject() { }
    [Test] public void SetSize_ChangesDimensions() { }
    [Test] public void GetHeight_ReturnsSampledHeight() { }
    [Test] public void SetHeightmap_ModifiesHeights() { }
    [Test] public void AddTerrainLayer_AddsLayer() { }
}

#if THEATRE_HAS_PROBUILDER
[TestFixture]
public class ProBuilderOpTests
{
    [Test] public void CreateShape_Cube_CreatesProBuilderMesh() { }
    [Test] public void CreateShape_Cylinder_CreatesProBuilderMesh() { }
    [Test] public void ExportMesh_SavesMeshAsset() { }
    [Test] public void CreateShape_MissingShape_ReturnsError() { }
}
#endif
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: `terrain_op` create → set_heightmap → get_height
5. Manual: `probuilder_op` create_shape cube → extrude_faces
