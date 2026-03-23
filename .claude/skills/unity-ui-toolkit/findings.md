# UI Toolkit Deep Reference

Compiled from Unity 6000.4.0f1 documentation and source analysis (March 2026).

## Contents

- [USS File Structure](#uss-file-structure)
- [Foldout](#foldout)
- [Two-Column Tool List](#two-column-tool-list)
- [ListView (Virtualized)](#listview-virtualized)
- [Scrollable Feed](#scrollable-feed)
- [Status Indicators](#status-indicators)
- [ViewData Persistence](#viewdata-persistence)
- [UXML Reference](#uxml-reference)

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

## Foldout

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

## Two-Column Tool List

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

Use `ScrollView` + `foreach` for lists up to ~100 items. Use `ListView` (virtualized) only for larger lists.

---

## ListView (Virtualized)

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

## Status Indicators

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

## ViewData Persistence

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
