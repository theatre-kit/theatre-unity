# UI Toolkit Findings — Quick Reference

Compiled from Unity 6000.4.0f1 documentation and source analysis (March 2026).

## Loading UXML + USS from a Package

```csharp
const string k_Uxml = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uxml";
const string k_Uss  = "Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.uss";

static readonly Lazy<VisualTreeAsset> s_Tree  = new(() => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml));
static readonly Lazy<StyleSheet>      s_Style = new(() => AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Uss));

public void CreateGUI()
{
    s_Tree.Value.CloneTree(rootVisualElement);
    rootVisualElement.styleSheets.Add(s_Style.Value);
    // wire up with rootVisualElement.Q<T>("name")
}
```

Pattern confirmed in `RenderPipelineConvertersEditor.cs` and multiple Unity built-in packages.

## USS vs Inline C# Styles

- **USS for:** repeated styles, dark/light theme vars, hover/focus states (can't be set inline)
- **Inline C# for:** truly dynamic values only (e.g., progress bar width at runtime)
- Inline style.color overrides USS but ignores pseudo-states — avoid for status indicators

## Toggle Internal Structure

```
Toggle (.unity-toggle)
  └── .unity-base-field__input
        ├── #unity-checkmark (.unity-toggle__checkmark)   ← 14×14 box
        └── .unity-base-field__label                      ← the text
```

BaseField lays out with label LEFT and input RIGHT. Setting `width` on the Toggle container
clips the label because there's no room for both checkmark and label text.

**Fix — separate container + bare Toggle:**
```csharp
var row = new VisualElement();
row.style.flexDirection = FlexDirection.Row;
row.style.alignItems = Align.Center;

var toggle = new Toggle();       // no label text!
toggle.style.flexShrink = 0;
toggle.style.width = 18;

var label = new Label("Tool Name");
label.style.flexShrink = 0;
label.style.width = 160;

row.Add(toggle);
row.Add(label);
```

## Foldout Structure

```
Foldout (.unity-foldout)
  ├── Toggle (.unity-foldout__toggle)    ← header, arrow + text
  └── VE (.unity-foldout__content)       ← children go here
```

- Cannot add elements to the header (it's built-in)
- Target with USS: `.unity-foldout__text`, `.unity-foldout__content`
- Set `foldout.viewDataKey = "unique-key"` → expansion state saved across domain reloads

## Two-Column Row (Toggle | Name | Description)

```css
.tool-row {
    flex-direction: row;
    align-items: flex-start;
    padding: 2px 4px;
}
.tool-toggle { flex-shrink: 0; width: 18px; margin-top: 1px; }
.tool-name   { width: 180px; flex-shrink: 0; font-size: 11px; white-space: nowrap; overflow: hidden; }
.tool-desc   { flex-grow: 1; flex-shrink: 1; min-width: 0; font-size: 10px; white-space: normal; }
```

`min-width: 0` on the description label is critical — without it, flex items won't shrink
below content size (auto), causing overflow.

## ListView vs ScrollView + foreach

- 37 tools → `ScrollView` + `foreach` is fine
- Use `ListView` (virtualized) only for > 100 items
- `fixedItemHeight` required for ListView performance

## Scrollable Feed

```csharp
var feed = new ScrollView(ScrollViewMode.Vertical);
feed.style.maxHeight = 160;
feed.viewDataKey = "theatre-activity-feed";
// Append: feed.Add(new Label(text)); feed.scrollOffset = new Vector2(0, float.MaxValue);
```

`maxHeight` is required — without it, ScrollView grows to fit all content.

## Status Indicators

Use CSS classes, not inline colors:
```csharp
_dot.RemoveFromClassList("status-running");
_dot.RemoveFromClassList("status-stopped");
_dot.AddToClassList(running ? "status-running" : "status-stopped");
```

```css
.status-running { background-color: #44FF44; }
.status-stopped { background-color: #FF4444; }
```

## Unity Built-in USS Color Variables

```css
color: var(--unity-colors-label-text);              /* #C4C4C4 dark */
background-color: var(--unity-colors-window-background);  /* #383838 dark */
background-color: var(--unity-colors-button-background);  /* #585858 dark */
border-color: var(--unity-colors-default-border);         /* #232323 dark */
```

These automatically adapt to dark/light mode.

## ViewData — Persistence Across Domain Reloads

```csharp
myFoldout.viewDataKey = "theatre-tool-overrides";  // saves open/closed
myScroll.viewDataKey  = "theatre-activity-scroll"; // saves scroll position
```

Supported elements: ScrollView, ListView, Foldout, TreeView, MultiColumnListView, TabView.
Editor-only (not runtime).

## Common Pitfalls

| Problem | Fix |
|---|---|
| Toggle label disappears when width set | Don't set width on Toggle; use separate container + bare Toggle + Label |
| Label text clipped in flex row | Add `min-width: 0; white-space: normal; overflow: visible;` to label |
| ScrollView doesn't scroll, grows instead | Add `max-height` or `height` to the ScrollView |
| Foldout content indented too much | Override `.unity-foldout__content { margin-left: 0; }` |
| Orphan checkboxes in row | BaseField label is left-side; use bare Toggle + separate Label in a row |
| Inline color won't show hover state | Use USS class + pseudo-class (`:hover`) instead |
| UXML path not found | Use `Packages/com.company.name/...` not `package://` |
