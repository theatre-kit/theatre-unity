---
name: unity-ui-toolkit
description: "Unity 6 UI Toolkit patterns for EditorWindow development. Auto-loads when
  working with VisualElement, USS stylesheets, UXML templates, CreateGUI, rootVisualElement,
  UI Toolkit, Toggle, Foldout, ListView, ScrollView, viewDataKey, StyleSheet,
  VisualTreeAsset, EditorWindow UI, USS variables, flex layout, UI Builder."
user-invocable: false
---

# Unity UI Toolkit (Unity 6)

Authoritative patterns for Unity 6000.4.0f1. Verified against Unity source code and official documentation (March 2026).

See [findings.md](findings.md) for full code examples: USS file structure, Foldout, ListView, ScrollView, two-column layouts, UXML templates, ViewData persistence.

## Setup: Loading UXML + USS

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

## Critical Pitfall: Toggle Width

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

## Common Pitfalls

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

## Built-in USS Variables

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
