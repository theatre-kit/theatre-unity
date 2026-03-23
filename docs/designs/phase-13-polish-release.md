# Design: Phase 13 — Polish & Release

## Overview

Harden the package for public release: error message audit, README with
installation guide, sample project, CHANGELOG, package validation, and
release workflow. Documentation site deferred.

---

## Implementation Units

### Unit 1: README

**File**: `Packages/com.theatre.toolkit/README.md`

Contents:
- **What is Theatre**: One-paragraph description (MCP server inside Unity Editor)
- **Features**: Bullet list of all tool groups with operation counts
- **Quick Start**: 3 steps — install via git URL, copy `.mcp.json` snippet, open Theatre panel
- **Installation**: Git URL (`https://github.com/theatre-kit/theatre-unity.git?path=Packages/com.theatre.toolkit`), OpenUPM option, manual download
- **.mcp.json setup**: Full JSON config snippet for Claude Code / Cursor / etc.
- **Tool Reference**: Table of all registered tools. The exact count depends on which optional packages are installed. With all packages: ~40 tools. Without optional packages: ~28 tools. The README should say "40+ tools" rather than citing a specific number. Grouped by category (Stage, Director, ECS)
- **Requirements**: Unity 6 (6000.0+), .NET Standard 2.1
- **Optional packages**: Table of optional packages and which tools they unlock (Timeline, ProBuilder, Entities, Addressables, etc.)
- **License**: MIT

**Acceptance Criteria**:
- [ ] README exists and renders in GitHub
- [ ] Installation instructions work (git URL resolves)
- [ ] .mcp.json snippet is correct

---

### Unit 2: CHANGELOG

**File**: `Packages/com.theatre.toolkit/CHANGELOG.md`

Follow [Keep a Changelog](https://keepachangelog.com/) format:

```markdown
# Changelog

## [0.1.0] - 2026-03-21

### Added
- Stage: scene_snapshot, scene_hierarchy, scene_inspect, scene_delta
- Stage: spatial_query (nearest, radius, overlap, raycast, linecast, path_distance, bounds)
- Stage: watch (create, remove, list, check) with SSE notifications
- Stage: action (teleport, set_property, set_active, set_timescale, pause, step, unpause, invoke_method)
- Stage: recording (start, stop, marker, list_clips, delete_clip, query_range, diff_frames, clip_info, analyze)
- Director: scene_op (10 operations), prefab_op (7 operations), batch
- Director: material_op, scriptable_object_op, physics_material_op
- Director: texture_op, sprite_atlas_op, audio_mixer_op
- Director: render_pipeline_op (URP/HDRP), addressable_op
- Director: animation_clip_op, animator_controller_op, blend_tree_op, timeline_op
- Director: tilemap_op, navmesh_op, terrain_op, probuilder_op
- Director: input_action_op, lighting_op, quality_op, project_settings_op, build_profile_op
- ECS: ecs_world, ecs_snapshot, ecs_inspect, ecs_query, ecs_action
- Editor UI: Theatre panel, Project Settings, keyboard shortcuts (F8/F9/Ctrl+Shift+T)
- Editor UI: Scene View gizmos with auto-fade, overlay, welcome dialog
- SQLite-backed recording with delta compression
- Streamable HTTP transport (MCP protocol)
- Domain reload survival via SessionState persistence
- 350+ unit tests
```

---

### Unit 3: Sample Project

**Directory**: `Packages/com.theatre.toolkit/Samples~/BasicSetup/`

Unity requires samples to be in `Samples~/` (the tilde means Unity
doesn't auto-import them — users import via Package Manager).

Contents:
- `BasicSetup.unity` — A minimal scene with a few GameObjects (Player, Enemy, Environment)
- `README.md` — Instructions for the sample: "Open scene, enter Play Mode, connect your MCP agent"

**Implementation Notes**:
- Keep the scene extremely simple — just enough to demonstrate the tools
- Include a basic Health MonoBehaviour script so `action:set_property` has something to modify
- Include a simple prefab in `Prefabs/` so prefab_op works out of the box

---

### Unit 4: Error Message Audit

**No new files** — this is a sweep across all existing tool handlers.

Spawn an agent to:
1. Grep all `ErrorResponse` calls in `Editor/Tools/`
2. Verify every error has:
   - A specific error `code` (not generic "error")
   - A descriptive `message` (not vague)
   - An actionable `suggestion` referencing a specific Theatre tool
3. Fix any that are vague or missing suggestions

**Acceptance Criteria**:
- [ ] Every `ErrorResponse` call has all 3 fields
- [ ] Every `suggestion` references a specific tool or action the agent can take

---

### Unit 5: Package Validation

**File**: `Packages/com.theatre.toolkit/package.json` (verify/update)

Ensure the package manifest is complete:
```json
{
  "name": "com.theatre.toolkit",
  "version": "0.1.0",
  "displayName": "Theatre",
  "description": "AI agent toolkit for Unity — spatial awareness and programmatic control via MCP.",
  "unity": "6000.0",
  "author": {
    "name": "Theatre",
    "url": "https://github.com/theatre-kit/theatre-unity"
  },
  "license": "MIT",
  "keywords": ["ai", "mcp", "agent", "spatial", "debug"],
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  },
  "samples": [
    {
      "displayName": "Basic Setup",
      "description": "Minimal scene with GameObjects for testing Theatre tools.",
      "path": "Samples~/BasicSetup"
    }
  ]
}
```

**Key additions**: `samples` array (for Package Manager import UI), `version` bump to `0.1.0`.

---

### Unit 6: LICENSE

**File**: `Packages/com.theatre.toolkit/LICENSE.md`

MIT license.

---

### Unit 7: .mcp.json Template

**File**: `.mcp.json` (project root — already may exist)

Ensure the template is correct and up-to-date:
```json
{
  "mcpServers": {
    "theatre": {
      "type": "http",
      "url": "http://localhost:9078/mcp"
    }
  }
}
```

---

### Unit 8: Domain Reload Stress Test

**No new files** — add tests to verify domain reload survival.

Add to `Tests/Editor/`:
- Test that `WatchPersistence.Save` + `Restore` round-trips 20 watches (matching `TheatreConfig` max watch count)
- Test that `RecordingPersistence.Save` + `Restore` round-trips active recording + clip index
- Test that `ActivityLog.Save` + `Restore` round-trips 100 entries

These verify the SessionState persistence that enables domain reload survival.

---

## Implementation Order

```
Unit 6: LICENSE (trivial)
Unit 7: .mcp.json template (trivial)
Unit 5: Package validation (update package.json)
Unit 2: CHANGELOG
Unit 1: README
Unit 3: Sample project
Unit 4: Error message audit (sweep)
Unit 8: Domain reload stress tests
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Verify README renders correctly (read it)
5. Verify package.json has samples array
6. Verify LICENSE exists
7. Verify .mcp.json at project root is correct
