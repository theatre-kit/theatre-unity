# Design: Phase 9a — Director: Tilemap & Navigation

## Overview

Two spatial world-building tools: `tilemap_op` for 2D tilemap painting
and management, and `navmesh_op` for NavMesh configuration and baking.

Both use built-in Unity modules (no optional packages needed):
- Tilemap: `UnityEngine.Tilemaps` + `com.unity.2d.tilemap` (installed)
- NavMesh: `UnityEngine.AI` + `UnityEditor.AI`

All tools live in `Editor/Tools/Director/` under `ToolGroup.DirectorSpatial`.

---

## Architecture

```
Editor/Tools/Director/
  TilemapOpTool.cs    — MCP tool: tilemap_op (9 operations)
  NavMeshOpTool.cs    — MCP tool: navmesh_op (6 operations)
```

---

## Implementation Units

### Unit 1: TilemapOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/TilemapOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

public static class TilemapOpTool
{
    private static readonly JToken s_inputSchema;
    static TilemapOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string SetTile(JObject args);
    internal static string SetTiles(JObject args);
    internal static string BoxFill(JObject args);
    internal static string FloodFill(JObject args);
    internal static string Clear(JObject args);
    internal static string GetTile(JObject args);
    internal static string GetUsedTiles(JObject args);
    internal static string CreateRuleTile(JObject args);
    internal static string SetTilemapLayer(JObject args);
}
```

**Registration**: name `"tilemap_op"`, group `ToolGroup.DirectorSpatial`.

#### `set_tile`
- Required: `tilemap_path` (hierarchy path to a Tilemap component),
  `position` ([x, y, z] cell position), `tile_asset` (asset path to a TileBase)
- Resolve tilemap: `ObjectResolver.Resolve(path)` → get `Tilemap` component
- Load tile: `AssetDatabase.LoadAssetAtPath<TileBase>(tileAsset)`
- `Undo.RecordObject(tilemap, "Theatre tilemap_op:set_tile")`
- `tilemap.SetTile(new Vector3Int(x, y, z), tile)`
- Response: `{ "result": "ok", "operation": "set_tile", "position": [x,y,z] }`

#### `set_tiles`
- Required: `tilemap_path`, `tile_asset`, `positions` (array of [x,y,z])
- Batch set: use `tilemap.SetTiles(positions, tiles)` for performance
  (both arrays must match length)
- Build `Vector3Int[]` and `TileBase[]` (all same tile)
- Response with count set

#### `box_fill`
- Required: `tilemap_path`, `tile_asset`, `start` ([x,y,z]), `end` ([x,y,z])
- `tilemap.BoxFill(position, tile, startX, startY, endX, endY)`
  Note: Unity's `BoxFill` signature is: `BoxFill(Vector3Int position, TileBase tile, int startX, int startY, int endX, int endY)`
  Actually, it's simpler to use a loop:
  ```csharp
  for (int x = startX; x <= endX; x++)
      for (int y = startY; y <= endY; y++)
          tilemap.SetTile(new Vector3Int(x, y, 0), tile);
  ```
  Or use the built-in `tilemap.BoxFill` if the API matches.
- Response with area filled

#### `flood_fill`
- Required: `tilemap_path`, `tile_asset`, `position` ([x,y,z])
- `tilemap.FloodFill(new Vector3Int(x, y, z), tile)`
- Response

#### `clear`
- Required: `tilemap_path`
- Optional: `region` (`{ "start": [x,y,z], "end": [x,y,z] }`) — if provided,
  clear only that region by setting tiles to null. If omitted, clear all.
- `tilemap.ClearAllTiles()` for full clear
- Or loop and `SetTile(pos, null)` for region clear
- Response

#### `get_tile` (READ-ONLY)
- Required: `tilemap_path`, `position` ([x,y,z])
- `var tile = tilemap.GetTile(new Vector3Int(x, y, z))`
- Response: `{ "result": "ok", "position": [x,y,z], "tile": "Assets/..." or null }`

#### `get_used_tiles` (READ-ONLY)
- Required: `tilemap_path`
- Optional: `budget` (token budget for response)
- `tilemap.CompressBounds()` first to tighten bounds
- Iterate `tilemap.cellBounds`, collect non-null tile positions
- Budget-limited response: `{ "result": "ok", "tiles": [{"position": [x,y,z], "tile": "..."}], "count": N }`

#### `create_rule_tile`
- Required: `asset_path` (must end in `.asset`), `default_tile` (asset path to default sprite/tile)
- Optional: `rules` (array of neighbor rule definitions)
- This is complex. For Phase 9a, implement a basic version:
  `ScriptableObject.CreateInstance<RuleTile>()`, set default sprite, save
- Rules can be added later via `set_fields` on the SO
- Response with asset_path

**Note**: `RuleTile` is in `UnityEngine.Tilemaps` (available via
`com.unity.2d.tilemap.extras` which is installed).

#### `set_tilemap_layer`
- Required: `tilemap_path`
- Optional: `sorting_layer` (string), `sorting_order` (int), `material` (asset path)
- Modify the `TilemapRenderer` component on the same GameObject
- Response

**JSON Schema** properties:
- `operation` (required, enum of 9 ops)
- `tilemap_path`, `position`, `tile_asset`, `positions`
- `start`, `end` (for box_fill/clear region)
- `asset_path`, `default_tile`, `rules`
- `sorting_layer`, `sorting_order`, `material`
- `budget`, `dry_run`

**Acceptance Criteria**:
- [ ] `set_tile` places a tile at a cell position
- [ ] `set_tiles` batch-places tiles
- [ ] `box_fill` fills a rectangular region
- [ ] `clear` removes all tiles
- [ ] `get_tile` reads the tile at a position
- [ ] `get_used_tiles` lists all occupied cells
- [ ] All mutations register Undo

---

### Unit 2: NavMeshOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/NavMeshOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.AI;

public static class NavMeshOpTool
{
    private static readonly JToken s_inputSchema;
    static NavMeshOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Bake(JObject args);
    internal static string SetArea(JObject args);
    internal static string AddModifier(JObject args);
    internal static string AddLink(JObject args);
    internal static string SetAgentType(JObject args);
    internal static string AddSurface(JObject args);
}
```

**Registration**: name `"navmesh_op"`, group `ToolGroup.DirectorSpatial`.

#### `bake`
- Optional: `agent_type_id` (int, default 0 — Humanoid)
- `NavMeshBuilder.BuildNavMesh()` — bakes the NavMesh synchronously
  Actually, the modern API is `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`
  or `NavMeshBuilder.ClearAllNavMeshes()` + `NavMeshBuilder.BuildNavMesh()`
- Response: `{ "result": "ok", "operation": "bake" }`

#### `set_area`
- Required: `index` (int, 0-31), `name` (string), `cost` (float)
- `var areas = GameObjectUtility.GetNavMeshAreaNames()` — read-only
- Use `SerializedObject` on `NavMeshProjectSettings` to modify areas:
  ```csharp
  var settings = NavMesh.GetSettingsCount() > 0 ? ... : ...
  ```
  Actually, NavMesh areas are set via:
  `GameObjectUtility.SetNavMeshArea(go, areaIndex)` — per-object
  Area names/costs are in project settings. Use:
  ```csharp
  var so = new SerializedObject(
      UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
  ```
  This is complex. Best effort — set area name and cost via SerializedObject
  on the NavMesh settings asset, or return error if API isn't accessible.

#### `add_modifier`
- Required: `path` (hierarchy path to GameObject)
- Optional: `area` (int), `ignore_from_build` (bool), `affect_children` (bool)
- Resolve GO, add `NavMeshModifier` component (if not already present)
- Set: `modifier.area = area`, `modifier.ignoreFromBuild = ignore`,
  `modifier.AffectChildren = affectChildren`
- `Undo.AddComponent` if adding
- Response

#### `add_link`
- Required: `start` ([x,y,z]), `end` ([x,y,z])
- Optional: `bidirectional` (bool, default true), `width` (float),
  `area` (int), `parent_path` (hierarchy path — where to attach the link GO)
- Create a new GameObject with `OffMeshLink` component:
  ```csharp
  var go = new GameObject("OffMeshLink");
  var link = go.AddComponent<OffMeshLink>();
  link.startTransform = ...; // set position
  link.endTransform = ...;
  link.biDirectional = bidirectional;
  link.area = area;
  ```
  Actually, OffMeshLink needs start/end transforms. Create two child
  GameObjects as markers, position them, assign to the link.
- `Undo.RegisterCreatedObjectUndo`
- Response with link path

#### `set_agent_type`
- Required: `agent_type_id` (int)
- Optional: `radius` (float), `height` (float), `step_height` (float),
  `max_slope` (float)
- Access via `NavMesh.GetSettingsByID(agentTypeId)` — read-only struct
- Modification requires `SerializedObject` on the settings asset
- Best effort — may need to use `NavMeshBuilder.navMeshSettingsObject`

#### `add_surface`
- Required: `path` (hierarchy path to GameObject)
- Optional: `collect_objects` ("all"/"volume"/"children"),
  `use_geometry` ("render_meshes"/"physics_colliders")
- Add `NavMeshSurface` component to the GO
- Note: `NavMeshSurface` is from `com.unity.ai.navigation` package.
  Check if it's installed. If not, return `package_not_installed` error.
- Set properties, `Undo.AddComponent`
- Response

**Implementation Note**: NavMesh APIs are split between:
- `UnityEngine.AI` (runtime: `NavMesh`, `NavMeshAgent`, `OffMeshLink`)
- `UnityEditor.AI` (editor: `NavMeshBuilder`)
- `Unity.AI.Navigation` (package: `NavMeshSurface`, `NavMeshModifier`)

The `NavMeshSurface` and `NavMeshModifier` classes are in the
`com.unity.ai.navigation` package, which may or may not be installed.
Guard with `#if` or use reflection. `OffMeshLink` is in the engine
(always available).

For `bake`, the simplest approach that works without the AI Navigation
package is `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`.

**JSON Schema** properties:
- `operation` (required, enum of 6 ops)
- `agent_type_id`, `radius`, `height`, `step_height`, `max_slope`
- `path`, `area`, `ignore_from_build`, `affect_children`
- `start`, `end`, `bidirectional`, `width`
- `collect_objects`, `use_geometry`
- `index`, `name`, `cost`
- `dry_run`

**Acceptance Criteria**:
- [ ] `bake` triggers NavMesh baking
- [ ] `add_link` creates an OffMeshLink between two points
- [ ] `add_modifier` adds NavMeshModifier to a GameObject (if available)
- [ ] `add_surface` adds NavMeshSurface (if package installed)
- [ ] Missing AI Navigation package returns clear error

---

### Unit 3: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add after Phase 8b registrations:
```csharp
TilemapOpTool.Register(registry);       // Phase 9a
NavMeshOpTool.Register(registry);       // Phase 9a
```

---

## Implementation Order

```
Unit 1: TilemapOpTool (independent)
Unit 2: NavMeshOpTool (independent)
Unit 3: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/SpatialBuildingToolTests.cs`

```csharp
[TestFixture]
public class TilemapOpTests
{
    // Note: Tilemap tests need a Grid + Tilemap in the scene
    // Create in SetUp, destroy in TearDown

    [Test] public void SetTile_PlacesTileAtPosition() { }
    [Test] public void Clear_RemovesAllTiles() { }
    [Test] public void GetTile_ReturnsNullForEmpty() { }
    [Test] public void GetUsedTiles_ReturnsOccupiedCells() { }
    [Test] public void SetTile_MissingTilemap_ReturnsError() { }
}

[TestFixture]
public class NavMeshOpTests
{
    [Test] public void Bake_TriggersWithoutError() { }
    [Test] public void AddLink_CreatesOffMeshLink() { }
    [Test] public void AddModifier_MissingPath_ReturnsError() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: create Grid + Tilemap, call `tilemap_op` set_tile with a tile asset
5. Manual: call `navmesh_op` bake on a scene with walkable geometry
