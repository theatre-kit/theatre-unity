# Skill: Unity UI Toolkit (Unity 6)

Authoritative patterns for building editor windows with UI Toolkit in Unity 6000.4.0f1.
Verified against Unity source code and official documentation (March 2026).

Load this skill before writing any UI Toolkit code for Theatre.

---

## Setup: Loading UXML + USS in an EditorWindow

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class TheatreWindow : EditorWindow
{
    [MenuItem("Window/Theatre")]
    public static void Show() => GetWindow<TheatreWindow>("Theatre");

    // Package paths: "Packages/com.company.name/..." format
    const string k_Uxml = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uxml";
    const string k_Uss  = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uss";

    // Lazy<> so AssetDatabase is only called after import completes
    static readonly Lazy<VisualTreeAsset> s_Tree =
        new(() => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml));
    static readonly Lazy<StyleSheet> s_Style =
        new(() => AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Uss));

    public void CreateGUI()
    {
        s_Tree.Value.CloneTree(rootVisualElement);
        rootVisualElement.styleSheets.Add(s_Style.Value);

        // Wire up named elements
        var statusLabel = rootVisualElement.Q<Label>("status-label");
        var myToggle    = rootVisualElement.Q<Toggle>("my-toggle");
    }
}
```

If using **C#-only** (no UXML), add USS like this:
```csharp
public void CreateGUI()
{
    rootVisualElement.styleSheets.Add(s_Style.Value);
    rootVisualElement.AddToClassList("theatre-window");
    // build elements in C#...
}
```

---

## USS File Structure

```css
/* Variables — define once, reuse everywhere */
:root {
    --theatre-stage-color:    #7ECFFF;
    --theatre-director-color: #7EFF9A;
    --theatre-watch-color:    #FFE566;
    --theatre-error-color:    #FF6666;
    --theatre-muted:          #666666;
}

/* Section header */
.section-header {
    font-style: bold;
    margin-top: 8px;
    margin-bottom: 2px;
    padding-bottom: 2px;
    border-bottom-width: 1px;
    border-bottom-color: #555555;
    color: var(--unity-colors-label-text);
}

/* Group header row (toggle + label) */
.group-row {
    flex-direction: row;
    align-items: center;
    margin: 2px 0;
}
.group-toggle {
    flex-shrink: 0;
    width: 18px;
}
.group-label {
    font-size: 11px;
    flex-shrink: 0;
    margin-left: 2px;
}

/* Two-column tool row: checkbox | name | description */
.tool-row {
    flex-direction: row;
    align-items: flex-start;
    padding: 2px 4px;
    margin: 1px 0;
}
.tool-row:hover {
    background-color: rgba(255, 255, 255, 0.05);
    border-radius: 2px;
}
.tool-toggle {
    flex-shrink: 0;
    width: 18px;
    margin-top: 1px;
    margin-right: 4px;
}
.tool-name {
    width: 180px;
    flex-shrink: 0;
    font-size: 11px;
    overflow: hidden;
    -unity-text-overflow-position: end;
    white-space: nowrap;
}
.tool-desc {
    flex-grow: 1;
    flex-shrink: 1;
    min-width: 0;        /* CRITICAL: allows shrink below content size */
    font-size: 10px;
    color: var(--theatre-muted);
    white-space: normal; /* allow text to wrap */
    overflow: visible;
}

/* Activity feed entries */
.activity-feed {
    background-color: rgba(0, 0, 0, 0.15);
    border-radius: 3px;
    padding: 2px;
}
.activity-entry {
    font-size: 10px;
    padding: 1px 2px;
    white-space: nowrap;
}
.activity-stage    { color: var(--theatre-stage-color); }
.activity-director { color: var(--theatre-director-color); }
.activity-watch    { color: var(--theatre-watch-color); }
.activity-error    { color: var(--theatre-error-color); }

/* Status indicator dot */
.status-dot {
    width: 8px;
    height: 8px;
    border-radius: 4px;
    flex-shrink: 0;
    margin-right: 6px;
    margin-top: 3px;
}
.status-running { background-color: #44CC44; }
.status-stopped { background-color: #CC4444; }
.status-warning { background-color: #CC8822; }
```

---

## Toggle: The Correct Patterns

### WRONG — sets width on Toggle, label disappears:
```csharp
var toggle = new Toggle("Spatial Queries");
toggle.style.width = 90;  // BAD: clips the label
```

### CORRECT — bare Toggle + separate Label in a row container:
```csharp
var row = new VisualElement();
row.AddToClassList("group-row");

var toggle = new Toggle();          // no label text
toggle.AddToClassList("group-toggle");
toggle.value = initialValue;
toggle.RegisterValueChangedCallback(OnToggleChanged);

var label = new Label("Spatial Queries");
label.AddToClassList("group-label");

row.Add(toggle);
row.Add(label);
```

### Why this happens (internal structure):
```
Toggle (.unity-toggle)
  └── .unity-base-field__input
        ├── #unity-checkmark   ← ~14px wide
        └── .unity-base-field__label  ← flex-grow: 1, takes remaining width
```
When you constrain Toggle width, the label (which is LEFT of the checkmark in BaseField layout)
gets squeezed to zero. The checkmark and label are siblings inside `__input`, not on opposite
sides of the Toggle.

---

## Foldout: Correct Usage

```csharp
var foldout = new Foldout();
foldout.text = "Individual Tool Overrides";
foldout.value = false;  // collapsed by default
foldout.viewDataKey = "theatre-tool-overrides-foldout";  // persist state

// Add children to the content area (done via foldout.Add()):
foldout.Add(new Label("child element"));

// Style the header and content via USS:
// .unity-foldout__text   → label text
// .unity-foldout__content → indented content area
// .unity-foldout__toggle → the arrow + text row
```

**You cannot add extra elements to the Foldout header.** It's a built-in Toggle.
For group headers needing toggle + extra controls, build a custom `VisualElement` row.

---

## Two-Column Tool List (C# only, 37 tools)

```csharp
private static VisualElement BuildToolList(
    IReadOnlyList<ToolInfo> tools,
    HashSet<string> disabled)
{
    var container = new VisualElement();

    foreach (var tool in tools)
    {
        var row = new VisualElement();
        row.AddToClassList("tool-row");

        var toggle = new Toggle();
        toggle.AddToClassList("tool-toggle");
        toggle.value = !disabled.Contains(tool.Name);
        var toolName = tool.Name;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
                TheatreConfig.DisabledTools.Remove(toolName);
            else
                TheatreConfig.DisabledTools.Add(toolName);
            TheatreServer.SseManager?.NotifyToolsChanged();
        });

        var nameLabel = new Label(tool.Name);
        nameLabel.AddToClassList("tool-name");

        var descLabel = new Label(tool.Description ?? string.Empty);
        descLabel.AddToClassList("tool-desc");

        row.Add(toggle);
        row.Add(nameLabel);
        row.Add(descLabel);
        container.Add(row);
    }

    return container;
}
```

---

## ListView with makeItem/bindItem (for large dynamic lists)

Use `ListView` only when you have > 100 items or need efficient virtualization.

```csharp
var list = new ListView();
list.makeItem = () =>
{
    var row = new VisualElement();
    row.AddToClassList("tool-row");
    row.Add(new Toggle  { name = "toggle" });
    row.Add(new Label   { name = "name"   });
    row.Add(new Label   { name = "desc"   });
    return row;
};
list.bindItem = (element, index) =>
{
    var tool = tools[index];
    element.Q<Toggle>("toggle").value = !disabled.Contains(tool.Name);
    element.Q<Label>("name").text = tool.Name;
    element.Q<Label>("desc").text = tool.Description;
};
list.itemsSource     = tools;
list.fixedItemHeight = 22;  // REQUIRED for virtualization
list.selectionType   = SelectionType.None;
```

---

## Scrollable Feed

```csharp
_feed = new ScrollView(ScrollViewMode.Vertical);
_feed.style.maxHeight = 160;   // REQUIRED — without this, grows to fit all content
_feed.viewDataKey = "theatre-activity-feed";

// Append entry:
var lbl = new Label(text);
lbl.AddToClassList("activity-entry");
lbl.AddToClassList(isError ? "activity-error" : "activity-stage");
_feed.Add(lbl);

// Scroll to bottom after adding:
_feed.schedule.Execute(() =>
    _feed.scrollOffset = new Vector2(0, float.MaxValue));
```

---

## Status Indicators (CSS class approach)

```csharp
// In CreateGUI:
_statusDot = new VisualElement();
_statusDot.AddToClassList("status-dot");

// In RefreshStatus:
_statusDot.RemoveFromClassList("status-running");
_statusDot.RemoveFromClassList("status-stopped");
_statusDot.AddToClassList(isRunning ? "status-running" : "status-stopped");

// NEVER do this — it bypasses USS:
// _statusDot.style.backgroundColor = running ? Color.green : Color.red;
```

---

## ViewData Persistence (Domain Reload Survival)

These elements automatically save/restore state when given a `viewDataKey`:

| Element | Persisted state |
|---|---|
| `Foldout` | Open/closed |
| `ScrollView` | Scroll offset |
| `ListView` | Selection index |
| `TreeView` | Selection |
| `TabView` | Selected tab |

```csharp
myFoldout.viewDataKey = "theatre-overrides-foldout";
myScroll.viewDataKey  = "theatre-activity-scroll";
```

Editor-only. viewDataKey must be unique per window instance.

---

## Common Pitfalls Reference

| Symptom | Root Cause | Fix |
|---|---|---|
| Toggle label invisible | `width` set on Toggle clips label | Use bare Toggle + separate Label in row container |
| Checkmark only, no label | BaseField layout not understood | Use separate row element; don't use `new Toggle("label")` with fixed width |
| Description text clipped | No `min-width: 0` on flex item | Add `min-width: 0` to the description label USS |
| ScrollView doesn't scroll | No height constraint | Add `max-height` to ScrollView |
| Foldout won't remember state | No `viewDataKey` | Assign unique `viewDataKey` |
| USS file not found | Wrong path format | Use `Packages/com.name/...` not `package://...` |
| Inline color ignores hover | Inline style overrides USS | Use USS class + `:hover` pseudo-class |
| `min-width: auto` prevents shrink | Flex default | Override with `min-width: 0` on shrinkable items |

---

## UXML Reference

```xml
<?xml version="1.0" encoding="utf-8"?>
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
    <!-- Stylesheets -->
    <Style src="/Packages/com.theatre.toolkit/Editor/UI/Theatre.uss"/>

    <!-- Named elements wired up in C# with Q<T>("name") -->
    <ui:VisualElement name="status-bar" class="status-bar">
        <ui:VisualElement name="status-dot" class="status-dot status-stopped"/>
        <ui:Label name="status-label" class="status-text"/>
        <ui:Button name="copy-url-btn" text="Copy URL"/>
    </ui:VisualElement>

    <ui:Label text="Tool Groups" class="section-header"/>
    <ui:VisualElement name="tool-groups-container"/>

    <ui:Label text="Agent Activity" class="section-header"/>
    <ui:ScrollView name="activity-feed" class="activity-feed"
                   view-data-key="theatre-activity-feed"
                   style="max-height: 160px;"/>
</ui:UXML>
```

---

## Built-in USS Variables (Dark Theme Values)

Use these to match the Unity editor theme automatically:

```css
/* Text */
color: var(--unity-colors-label-text);        /* #C4C4C4 */
color: var(--unity-colors-default-text);      /* #D2D2D2 */
color: var(--unity-colors-error-text);        /* #D32222 */
color: var(--unity-colors-warning-text);      /* #F4BC02 */

/* Backgrounds */
background-color: var(--unity-colors-window-background);     /* #383838 */
background-color: var(--unity-colors-default-background);    /* #282828 */
background-color: var(--unity-colors-button-background);     /* #585858 */
background-color: var(--unity-colors-input_field-background); /* #2A2A2A */

/* Borders */
border-color: var(--unity-colors-default-border);  /* #232323 */

/* Hover states */
background-color: var(--unity-colors-button-background-hover);    /* #676767 */
background-color: var(--unity-colors-button-background-pressed);  /* #46607C */
border-color: var(--unity-colors-input_field-border-focus);       /* #3A79BB */
```
