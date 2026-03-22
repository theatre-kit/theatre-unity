# Unity 6 UI Toolkit — Research Findings

Researched for Theatre Window rewrite (Unity 6000.4.0f1, March 2026).

## 1. Core Architecture

UI Toolkit windows are built with three complementary layers:

- **UXML** — declarative structure (XML-based, like HTML)
- **USS** — styling (CSS-like, but Unity-specific)
- **C#** — behavior, data binding, dynamic updates

For an editor window, the entry point is `CreateGUI()` on `EditorWindow`. It receives `rootVisualElement` and you build the tree from there.

### Loading UXML + USS from a package

```csharp
const string k_Uxml = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uxml";
const string k_Uss  = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uss";

// Lazy load (cached after first access)
static readonly Lazy<VisualTreeAsset> s_Tree =
    new(() => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml));
static readonly Lazy<StyleSheet> s_Style =
    new(() => AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Uss));

public void CreateGUI()
{
    s_Tree.Value.CloneTree(rootVisualElement);
    rootVisualElement.styleSheets.Add(s_Style.Value);
    // Now wire up callbacks with rootVisualElement.Q<T>("name")
}
```

This is the pattern used by Unity's own built-in packages (RenderPipelineConvertersEditor,
EntityJournalingWindow, etc.).

---

## 2. USS Fundamentals

### Syntax

USS is CSS-like with Unity-specific extensions:

```css
/* Type selector */
Label { color: white; }

/* Class selector */
.theatre-section-header {
    font-style: bold;
    border-bottom-width: 1px;
    border-bottom-color: #666;
    margin-top: 6px;
    margin-bottom: 2px;
}

/* Name selector (element name=) */
#status-label { flex-grow: 1; }

/* Descendant selector */
.tool-row Label { font-size: 11px; }

/* Child selector */
.group-header > Toggle { flex-shrink: 0; }

/* Pseudo-class */
Button:hover { background-color: var(--unity-colors-button-background-hover); }
```

### USS vs Inline Styles (C#)

**Use USS for:**
- Any style that applies to multiple elements
- Dark/light theme adaptation
- Hover/focus/active states (these CANNOT be set inline)
- Any layout that will be reused

**Use inline C# style only for:**
- Dynamic runtime values (e.g., a progress bar width based on a float)
- One-off elements with no reuse potential

**Pitfall:** `element.style.color = new Color(...)` sets an inline style that overrides USS but
cannot respond to pseudo-states. Use USS + `AddToClassList("error-state")` instead.

### Loading USS in UXML

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <Style src="/Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uss"/>
    <!-- elements... -->
</ui:UXML>
```

Package paths use the `com.company.package` format starting from `Packages/`.

### USS Variables (Custom Properties)

```css
/* Define in :root for global scope */
:root {
    --theatre-stage-color: #7ECFFF;
    --theatre-director-color: #7EFF9A;
    --theatre-error-color: #FF6666;
}

/* Reference with var() */
.stage-label { color: var(--theatre-stage-color); }
```

### Unity Built-in USS Variables

Unity provides theme-aware variables that automatically adapt to dark/light mode:

| Variable | Purpose | Dark value |
|---|---|---|
| `--unity-colors-default-text` | Default text | `#D2D2D2` |
| `--unity-colors-label-text` | Label text | `#C4C4C4` |
| `--unity-colors-button-text` | Button text | `#EEEEEE` |
| `--unity-colors-error-text` | Error text | `#D32222` |
| `--unity-colors-warning-text` | Warning text | `#F4BC02` |
| `--unity-colors-default-background` | Default background | `#282828` |
| `--unity-colors-window-background` | Window background | `#383838` |
| `--unity-colors-button-background` | Button background | `#585858` |
| `--unity-colors-button-background-hover` | Button hover | `#676767` |
| `--unity-colors-input_field-background` | Input background | `#2A2A2A` |
| `--unity-colors-default-border` | Border | `#232323` |

---

## 3. Flex Layout

UI Toolkit uses a Flexbox layout engine (Yoga). Key properties:

```css
/* Row of children (default for VisualElement: column) */
.tool-row {
    flex-direction: row;
    align-items: center;   /* vertical alignment in a row */
}

/* Fill remaining space */
.tool-name { flex-grow: 1; }

/* Fixed size, don't shrink */
.tool-toggle { flex-shrink: 0; width: 16px; }

/* Two-column layout: fixed left + fill right */
.tool-entry {
    flex-direction: row;
    align-items: flex-start;
    padding: 2px 4px;
}
.tool-entry .col-name  { width: 160px; flex-shrink: 0; }
.tool-entry .col-desc  { flex-grow: 1; flex-shrink: 1; white-space: normal; }
```

### Default VisualElement behavior

- `flex-direction: column` (children stack vertically)
- `flex-grow: 0` (does NOT expand to fill parent)
- `flex-shrink: 1` (will shrink if needed)
- `width/height: auto` (sized by content)

---

## 4. Toggle — Internal Structure and Pitfalls

### Internal USS structure

```
Toggle (.unity-toggle)
  └── VisualElement (.unity-base-field__input)
        ├── VisualElement (#unity-checkmark, .unity-toggle__checkmark)   ← 14x14px box
        └── Label (.unity-base-field__label, .unity-toggle__label)        ← text
```

Toggle inherits from `BaseField<bool>`, which has this layout:
- The `BaseField` root has `flex-direction: row`
- The **label** is the text beside the checkbox (set via `new Toggle("Label text")`)
- The **input** (`.unity-base-field__input`) contains the checkmark

### The width-causes-label-disappear bug

**Root cause:** When you set `toggle.style.width = 90`, you constrain the entire Toggle container.
The internal label (`unity-base-field__label`) has `flex-grow: 1`, so it fills the Toggle's
width minus the checkmark. But if the total width is too small for checkmark + label, the label
clips or disappears because `overflow: hidden` on the container.

**Worse:** `BaseField` lays out with label on the LEFT at a fixed width, input on the right. For
`Toggle`, this means the label (the text you want to see) is actually the LEFT element, and the
checkmark is inside the RIGHT element. Setting a narrow total width clips the label.

**Fix: Don't set width on Toggle itself. Use a container row:**

```csharp
// WRONG - clips label:
var toggle = new Toggle("Spatial Queries");
toggle.style.width = 90;  // DON'T DO THIS

// RIGHT - separate container holds the sizing:
var row = new VisualElement();
row.style.flexDirection = FlexDirection.Row;
row.style.alignItems = Align.Center;
row.style.width = 120;  // set width on the container

var toggle = new Toggle();  // no label text
toggle.style.flexShrink = 0;
toggle.style.width = 18;  // just the checkmark area

var label = new Label("Spatial Queries");
label.style.flexGrow = 1;
label.style.overflow = Overflow.Hidden;
row.Add(toggle);
row.Add(label);
```

**Alternative fix: let Toggle use natural size with flex-shrink:**

```csharp
// The Toggle fills all available space — works when container is flex-row:
var toggle = new Toggle("Spatial Queries");
toggle.style.minWidth = 80;  // minimum, but can grow
toggle.style.flexGrow = 0;
toggle.style.flexShrink = 0;
```

### USS to fix Toggle label behavior

```css
/* Make the label inside Toggle wrap instead of clip */
.unity-toggle > .unity-base-field__label {
    white-space: normal;
    overflow: visible;
    flex-shrink: 1;
}
```

### viewDataKey for toggle persistence

```csharp
toggle.viewDataKey = "theatre-toggle-spatial-queries";
// Foldout open/close state and ScrollView position also persist via viewDataKey
```

---

## 5. Foldout — Structure and Pitfalls

### Internal structure

```
Foldout (.unity-foldout)
  ├── Toggle (.unity-foldout__toggle, .unity-toggle)   ← the header arrow+label
  └── VisualElement (.unity-foldout__content)           ← children go here
```

### Key USS classes for styling

```css
/* Style the header toggle */
.unity-foldout__toggle { height: 22px; }

/* Style the expand arrow */
.unity-foldout__checkmark { }

/* Style the header label text */
.unity-foldout__text { font-style: bold; }

/* Style the content area */
.unity-foldout__content { margin-left: 16px; }
```

### Pitfalls

1. **Direct child access:** Elements added to a Foldout go into `contentContainer`
   (which is `.unity-foldout__content`), not directly under the Foldout. Use
   `foldout.Q(".unity-foldout__content")` to style the container.

2. **You cannot add elements to the header.** The header is a built-in Toggle.
   If you need a toggle + extra controls in the header, build a custom `VisualElement`
   instead of using `Foldout`.

3. **viewDataKey for persistence** across domain reloads:
   ```csharp
   foldout.viewDataKey = "theatre-tool-overrides-foldout";
   ```
   The expansion state is saved to `EditorPrefs` automatically.

### Group header with a toggle using custom VisualElement (recommended for Theatre)

```csharp
private static VisualElement MakeGroupHeader(string groupName, ToolGroup flag, ref ToolGroup current)
{
    var header = new VisualElement();
    header.AddToClassList("theatre-group-header");

    var toggle = new Toggle();
    toggle.value = (current & flag) != 0;
    toggle.style.flexShrink = 0;
    var capturedFlag = flag;
    toggle.RegisterValueChangedCallback(evt => {
        var g = TheatreConfig.EnabledGroups;
        if (evt.newValue) g |= capturedFlag; else g &= ~capturedFlag;
        TheatreServer.SetEnabledGroups(g);
    });
    header.Add(toggle);

    var label = new Label(groupName);
    label.AddToClassList("theatre-group-label");
    header.Add(label);

    return header;
}
```

---

## 6. Two-Column Layout Pattern

The recommended pattern for a tool name + description layout:

### C# approach (ScrollView + foreach for ~37 tools)

```csharp
private static VisualElement BuildToolList(IEnumerable<ToolInfo> tools)
{
    var container = new VisualElement();
    container.AddToClassList("tool-list");

    foreach (var tool in tools)
    {
        var row = new VisualElement();
        row.AddToClassList("tool-row");

        var toggle = new Toggle();
        toggle.value = !disabled.Contains(tool.Name);
        toggle.AddToClassList("tool-toggle");
        toggle.RegisterValueChangedCallback(evt => { /* ... */ });

        var nameLabel = new Label(tool.Name);
        nameLabel.AddToClassList("tool-name");

        var descLabel = new Label(tool.Description);
        descLabel.AddToClassList("tool-desc");

        row.Add(toggle);
        row.Add(nameLabel);
        row.Add(descLabel);
        container.Add(row);
    }
    return container;
}
```

### USS for two-column layout

```css
.tool-list {
    padding: 2px 0;
}

.tool-row {
    flex-direction: row;
    align-items: flex-start;
    padding: 2px 4px;
    min-height: 18px;
}

.tool-row:hover {
    background-color: rgba(255, 255, 255, 0.05);
}

.tool-toggle {
    flex-shrink: 0;
    width: 18px;
    margin-top: 1px;
}

.tool-name {
    width: 180px;
    flex-shrink: 0;
    font-size: 11px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.tool-desc {
    flex-grow: 1;
    flex-shrink: 1;
    font-size: 10px;
    color: var(--unity-colors-label-text);
    white-space: normal;       /* allow text wrap */
    overflow: visible;
}
```

### UXML approach for list item template

```xml
<!-- ToolListItem.uxml -->
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement class="tool-row">
        <ui:Toggle name="toggle" class="tool-toggle"/>
        <ui:Label name="tool-name" class="tool-name"/>
        <ui:Label name="tool-desc" class="tool-desc"/>
    </ui:VisualElement>
</ui:UXML>
```

Used in ListView:
```csharp
var listView = new ListView();
listView.makeItem = () => s_ItemTemplate.Value.CloneTree().Q("tool-row");
listView.bindItem = (element, index) => {
    element.Q<Toggle>("toggle").value = !disabled.Contains(tools[index].Name);
    element.Q<Label>("tool-name").text = tools[index].Name;
    element.Q<Label>("tool-desc").text = tools[index].Description;
};
listView.itemsSource = tools;
listView.fixedItemHeight = 22;
```

### ListView vs ScrollView + foreach

| Approach | Use when |
|---|---|
| `ListView` (virtualized) | > 100 items, need scrolling, performance matters |
| `ScrollView` + `foreach` | ≤ 50 static items, simpler code preferred |

For 37 tools, `ScrollView` + `foreach` is fine. Use `ListView` if the list
becomes dynamic or large.

---

## 7. Scrollable Activity Feed

```csharp
var feed = new ScrollView(ScrollViewMode.Vertical);
feed.style.maxHeight = 160;
feed.AddToClassList("activity-feed");

// Append an entry
var entry = new Label(text);
entry.AddToClassList("activity-entry");
entry.AddToClassList(isError ? "activity-error" : "activity-stage");
feed.Add(entry);

// Scroll to bottom
feed.scrollOffset = new Vector2(0, float.MaxValue);
```

```css
.activity-feed {
    background-color: rgba(0, 0, 0, 0.2);
    border-radius: 3px;
    padding: 2px;
}

.activity-entry {
    font-size: 10px;
    padding: 1px 2px;
    white-space: nowrap;
}

.activity-error  { color: #FF6666; }
.activity-stage  { color: #7ECFFF; }
.activity-director { color: #7EFF9A; }
.activity-watch  { color: #FFE566; }
```

### ViewData persistence for scroll position

```csharp
feed.viewDataKey = "theatre-activity-feed";
// ScrollView restores scroll position across domain reloads
```

---

## 8. Status Indicators with Color

**Do not use inline styles for status colors.** Use USS classes toggled by C#:

```css
/* USS */
.status-dot { width: 8px; height: 8px; border-radius: 4px; }
.status-running  { background-color: #44FF44; }
.status-stopped  { background-color: #FF4444; }
.status-warning  { background-color: #FFAA22; }
```

```csharp
// C# - swap class instead of setting style.backgroundColor
void UpdateStatus(bool running)
{
    _statusDot.RemoveFromClassList("status-running");
    _statusDot.RemoveFromClassList("status-stopped");
    _statusDot.AddToClassList(running ? "status-running" : "status-stopped");
}
```

---

## 9. Common Layout Pitfalls

### 1. Orphan checkboxes in flex row

**Problem:** Adding `Toggle` objects to a `flex-direction: row` container sometimes shows only
the checkmark, label invisible.

**Cause:** The Toggle's internal `BaseField` layout puts the label on the LEFT at a wide default
width, and when the outer flex row is too narrow, the label is off-screen or clipped.

**Fix:** Use `.unity-base-field__aligned` class (aligns to Inspector standard), or build
toggle+label manually as separate elements.

### 2. Label text clipping in flex items

**Problem:** Description text gets cut off.

**Fix:**
```css
.desc-label {
    flex-shrink: 1;
    white-space: normal;   /* allow wrap */
    overflow: visible;     /* don't clip */
    min-width: 0;          /* critical: allow shrinking below content size */
}
```

The `min-width: 0` override is critical. By default, flex items have
`min-width: auto` which prevents them from shrinking below their content size.

### 3. ScrollView inside flex column not getting height

**Problem:** `ScrollView` inside a flex column doesn't scroll — it just grows to fit all content.

**Fix:** Set `max-height` (or `height`) on the ScrollView:
```css
.watches-scroll { max-height: 120px; flex-shrink: 1; }
```
Or in C#: `scroll.style.maxHeight = 120;`

### 4. Foldout content margin

Default Foldout indents content by 15px. To remove or change it:
```css
.unity-foldout__content { margin-left: 0; padding-left: 8px; }
```

### 5. VisualElement not expanding to fill window width

**Problem:** Root container doesn't fill the window.

**Fix:** The rootVisualElement already does this. Child containers need
`flex-grow: 1` if they should fill vertically.

---

## 10. ViewData Persistence

These elements restore their state across domain reloads **automatically** when given a
`viewDataKey`:

| Element | What persists |
|---|---|
| `ScrollView` | Scroll position |
| `ListView` | Selection |
| `Foldout` | Open/closed state |
| `TreeView` | Selection |
| `MultiColumnListView` | Selection, column order/width |
| `TabView` | Selected tab |

```csharp
// Set in C# or UXML attribute view-data-key="..."
myFoldout.viewDataKey = "theatre-per-tool-overrides";
myScroll.viewDataKey  = "theatre-activity-scroll";
```

**ViewData only works in Editor UI, not runtime.**

---

## 11. Complete Minimal Example (C# only, no UXML)

Pattern confirmed from Unity's own packages:

```csharp
public class TheatreWindow : EditorWindow
{
    [MenuItem("Window/Theatre")]
    public static void Show() => GetWindow<TheatreWindow>("Theatre");

    const string k_Uss = "Packages/com.theatre.toolkit/Editor/UI/Theatre.uss";
    static readonly Lazy<StyleSheet> s_Style =
        new(() => AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Uss));

    public void CreateGUI()
    {
        rootVisualElement.styleSheets.Add(s_Style.Value);
        rootVisualElement.AddToClassList("theatre-window");

        // Section header
        rootVisualElement.Add(MakeSectionLabel("Tool Groups"));

        // Group with toggle
        var groupRow = new VisualElement();
        groupRow.AddToClassList("group-row");

        var groupToggle = new Toggle();
        groupToggle.value = true;
        groupToggle.AddToClassList("group-toggle");
        groupRow.Add(groupToggle);

        var groupLabel = new Label("Stage");
        groupLabel.AddToClassList("group-label");
        groupRow.Add(groupLabel);

        rootVisualElement.Add(groupRow);

        // Per-tool foldout (state saved across domain reloads)
        var foldout = new Foldout { text = "Individual Tools", value = false };
        foldout.viewDataKey = "theatre-individual-tools-foldout";
        foldout.AddToClassList("tool-foldout");

        foreach (var tool in GetAllTools())
        {
            var row = new VisualElement();
            row.AddToClassList("tool-row");

            var t = new Toggle();
            t.value = !IsDisabled(tool.Name);
            t.AddToClassList("tool-toggle");
            var captured = tool.Name;
            t.RegisterValueChangedCallback(evt => SetToolEnabled(captured, evt.newValue));

            var name = new Label(tool.Name);
            name.AddToClassList("tool-name");

            var desc = new Label(tool.Description);
            desc.AddToClassList("tool-desc");

            row.Add(t);
            row.Add(name);
            row.Add(desc);
            foldout.Add(row);
        }
        rootVisualElement.Add(foldout);

        // Activity feed
        var feedScroll = new ScrollView(ScrollViewMode.Vertical);
        feedScroll.style.maxHeight = 160;
        feedScroll.viewDataKey = "theatre-activity-feed-scroll";
        rootVisualElement.Add(feedScroll);
    }
}
```
