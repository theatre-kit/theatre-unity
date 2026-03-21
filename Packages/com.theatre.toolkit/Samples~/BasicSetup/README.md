# Basic Setup Sample

This sample provides a `Health` MonoBehaviour and instructions for building a minimal test scene to explore Theatre tools.

## Setup

1. **Import the sample** via Package Manager: select the Theatre package, expand Samples, and click Import next to Basic Setup.

2. **Create a test scene** manually:
   - Create a new scene (**File > New Scene**)
   - Add a few GameObjects: a Player, an Enemy, and some environment objects
   - Attach `Health.cs` to the Player and Enemy objects
   - Position them at different locations in the scene
   - Save the scene

3. **Enter Play Mode** — Theatre's Stage tools work at runtime.

4. **Connect your MCP client** — ensure `.mcp.json` at your project root points to `http://localhost:9078/mcp`.

## Trying the Tools

Once in Play Mode with the scene open, try these tools in your AI agent:

- `scene_snapshot` — get a budgeted overview of all GameObjects
- `scene_hierarchy {"operation": "list"}` — navigate the hierarchy
- `scene_inspect {"path": "/Player"}` — inspect the Player and its Health component
- `spatial_query {"operation": "nearest", "origin": [0, 0, 0], "limit": 5}` — find nearest objects
- `action {"operation": "set_property", "path": "/Player", "component": "Health", "property": "currentHp", "value": 50}` — modify a property

## Files

- `Scripts/Health.cs` — simple health component with `TakeDamage` and `Heal` methods
