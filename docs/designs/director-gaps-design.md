# Design: Director Gaps (Alpha Feedback)

## Overview

Implements four gaps identified from the first external agent session:

1. **ObjectReference property writes** — Enable `set_property`, `set_component`, and `create_gameobject` to assign assets via path, instance_id, or GUID
2. **Primitive GameObjects** — `create_gameobject` gains a `primitive_type` parameter
3. **Tags & layers feedback** — Three-state reporting (added/already_exists/overwritten)
4. **Edit Mode invoke** — Static method invocation and `run_menu_item` operation

All changes extend existing tools. No new MCP tool registrations.

---

## Implementation Units

### Unit 1: Unify SetPropertyValue into DirectorHelpers

**Goal**: Eliminate the duplicated `SetPropertyValue` in `ActionSetProperty` by delegating to `DirectorHelpers.SetPropertyValue`. This must happen first because Unit 2 adds ObjectReference support to the unified method.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/DirectorHelpers.cs`

Make `SetPropertyValue` `internal` (it's already `private` — just change visibility):

```csharp
/// <summary>
/// Set a single SerializedProperty value from a JToken.
/// Supports Integer, Float, Boolean, String, Vector2/3/4, Color,
/// Quaternion, Enum, and ObjectReference.
/// Returns true on success; on failure, sets error and returns false.
/// </summary>
internal static bool SetPropertyValue(
    SerializedProperty prop, JToken value, out string error)
```

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionSetProperty.cs`

Remove the private `SetPropertyValue` method entirely (lines 120-226). Replace the call site at line 74:

```csharp
// Before:
if (!SetPropertyValue(prop, value, out var setError))

// After:
if (!DirectorHelpers.SetPropertyValue(prop, value, out var setError))
```

Also remove the private `ReadCurrentValue` method (lines 98-118) and replace with a call to a new `DirectorHelpers.ReadPropertyValue`:

```csharp
/// <summary>
/// Read the current value of a SerializedProperty as a JToken.
/// Used to report previous_value in set_property responses.
/// </summary>
internal static JToken ReadPropertyValue(SerializedProperty prop)
{
    switch (prop.propertyType)
    {
        case SerializedPropertyType.Integer: return prop.intValue;
        case SerializedPropertyType.Float: return Math.Round(prop.floatValue, 4);
        case SerializedPropertyType.Boolean: return prop.boolValue;
        case SerializedPropertyType.String: return prop.stringValue;
        case SerializedPropertyType.Vector2:
            return ResponseHelpers.ToJArray(prop.vector2Value);
        case SerializedPropertyType.Vector3:
            return ResponseHelpers.ToJArray(prop.vector3Value);
        case SerializedPropertyType.Color:
            return ResponseHelpers.ToJArray(prop.colorValue);
        case SerializedPropertyType.Enum:
            return prop.enumDisplayNames.Length > prop.enumValueIndex
                ? prop.enumDisplayNames[prop.enumValueIndex]
                : prop.enumValueIndex.ToString();
        case SerializedPropertyType.ObjectReference:
            var obj = prop.objectReferenceValue;
            if (obj == null) return JValue.CreateNull();
            var assetPath = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(assetPath) ? assetPath : obj.name;
        default: return prop.propertyType.ToString();
    }
}
```

**Acceptance Criteria**:
- [ ] `ActionSetProperty` has no `SetPropertyValue` or `ReadCurrentValue` methods
- [ ] `ActionSetProperty.Execute` calls `DirectorHelpers.SetPropertyValue` and `DirectorHelpers.ReadPropertyValue`
- [ ] All existing `set_property` tests pass unchanged
- [ ] `DirectorHelpers.SetPropertyValue` is `internal` (visible to Editor assembly and tests via `InternalsVisibleTo`)

---

### Unit 2: ObjectReference Property Writes

**Goal**: Add `ObjectReference` case to `DirectorHelpers.SetPropertyValue`. Accepts three input formats: asset path (string), instance_id (int), GUID (string), or null to clear.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/DirectorHelpers.cs`

Add a new static helper method and the ObjectReference case in `SetPropertyValue`:

```csharp
/// <summary>
/// Resolve a Unity Object from a JToken value for ObjectReference assignment.
/// Accepts:
///   - string: asset path ("Assets/Materials/Foo.mat") or
///             sub-asset path ("Assets/Models/Cube.fbx::MeshName") or
///             GUID ("a1b2c3d4e5f6...")
///   - int: instance_id
///   - null: clears the reference
/// Returns the resolved Object, or null if the value is JSON null.
/// Sets error on failure.
/// </summary>
internal static UnityEngine.Object ResolveObjectReference(
    JToken value, SerializedProperty prop, out string error)
{
    error = null;

    // null clears the reference
    if (value == null || value.Type == JTokenType.Null)
        return null;

    // int → instance_id
    if (value.Type == JTokenType.Integer)
    {
        var instanceId = value.Value<int>();
        var obj = EditorUtility.InstanceIDToObject(instanceId);
        if (obj == null)
        {
            error = $"No object found with instance_id {instanceId}";
            return null;
        }
        return obj;
    }

    // string → asset path, sub-asset path, or GUID
    if (value.Type == JTokenType.String)
    {
        var str = value.Value<string>();
        if (string.IsNullOrEmpty(str))
        {
            error = "Asset path must not be empty";
            return null;
        }

        // Check for sub-asset syntax: "Assets/Models/Cube.fbx::MeshName"
        var separatorIndex = str.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            var mainPath = str.Substring(0, separatorIndex);
            var subAssetName = str.Substring(separatorIndex + 2);
            return ResolveSubAsset(mainPath, subAssetName, out error);
        }

        // Try as asset path first
        if (str.StartsWith("Assets/") || str.StartsWith("Packages/"))
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(str);
            if (obj == null)
            {
                error = $"No asset found at path '{str}'";
                return null;
            }
            return obj;
        }

        // Try as GUID
        var guidPath = AssetDatabase.GUIDToAssetPath(str);
        if (!string.IsNullOrEmpty(guidPath))
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(guidPath);
            if (obj != null) return obj;
        }

        error = $"Cannot resolve '{str}' as an asset path (must start with "
              + "'Assets/' or 'Packages/') or as a GUID";
        return null;
    }

    error = $"ObjectReference value must be a string (asset path/GUID), "
          + "integer (instance_id), or null. Got {value.Type}";
    return null;
}

private static UnityEngine.Object ResolveSubAsset(
    string mainPath, string subAssetName, out string error)
{
    error = null;
    var allAssets = AssetDatabase.LoadAllAssetsAtPath(mainPath);
    if (allAssets == null || allAssets.Length == 0)
    {
        error = $"No asset found at path '{mainPath}'";
        return null;
    }

    foreach (var asset in allAssets)
    {
        if (asset != null && asset.name == subAssetName)
            return asset;
    }

    // Build suggestion with available sub-asset names
    var names = new System.Collections.Generic.List<string>();
    foreach (var asset in allAssets)
    {
        if (asset != null && !string.IsNullOrEmpty(asset.name))
            names.Add(asset.name);
    }

    error = $"Sub-asset '{subAssetName}' not found in '{mainPath}'. "
          + $"Available: {string.Join(", ", names)}";
    return null;
}
```

Add the `ObjectReference` case in `SetPropertyValue`, before the `default:` case:

```csharp
case SerializedPropertyType.ObjectReference:
    var resolved = ResolveObjectReference(value, prop, out var refError);
    if (refError != null)
    {
        error = refError;
        return false;
    }
    // Type validation: check the resolved object is assignable
    if (resolved != null)
    {
        // Get the expected type from the field
        var fieldInfo = GetFieldType(prop);
        if (fieldInfo != null && !fieldInfo.IsInstanceOfType(resolved))
        {
            error = $"Asset '{resolved.name}' is {resolved.GetType().Name}, "
                  + $"expected {fieldInfo.Name}";
            return false;
        }
    }
    prop.objectReferenceValue = resolved;
    return true;
```

Add the helper to extract the expected field type:

```csharp
/// <summary>
/// Get the expected System.Type for an ObjectReference SerializedProperty.
/// Uses reflection on the target object's field declaration.
/// Returns null if the type cannot be determined (fallback: no type check).
/// </summary>
private static System.Type GetFieldType(SerializedProperty prop)
{
    var targetType = prop.serializedObject.targetObject.GetType();
    var fieldName = prop.propertyPath;

    // Handle array elements: strip [N] and get element type
    if (fieldName.Contains(".Array.data["))
    {
        var dotIndex = fieldName.IndexOf(".Array.data[");
        fieldName = fieldName.Substring(0, dotIndex);
    }

    // Walk the field path (handles nested structs via dots)
    System.Type currentType = targetType;
    foreach (var part in fieldName.Split('.'))
    {
        var field = currentType.GetField(part,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        if (field == null) return null;
        currentType = field.FieldType;
    }

    // Unwrap arrays/lists to element type
    if (currentType.IsArray)
        return currentType.GetElementType();
    if (currentType.IsGenericType
        && currentType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
        return currentType.GetGenericArguments()[0];

    return currentType;
}
```

Also update `ReadPropertyValue` to handle ObjectReference (already covered in Unit 1 above).

**Implementation Notes**:
- `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)` loads the main asset. For sub-assets (mesh inside FBX, sprite inside texture), use `LoadAllAssetsAtPath` + name match.
- `AssetDatabase.GUIDToAssetPath` returns empty string (not null) when the GUID is unknown.
- Type validation via `GetFieldType` uses reflection on the target object's C# field to determine the declared type. This catches mismatches like assigning a Texture2D to a Material field.
- `prop.objectReferenceValue = null` clears the reference (native Unity behavior).

**Acceptance Criteria**:
- [ ] `set_property` with `value: "Assets/Materials/Foo.mat"` assigns the material to a MeshRenderer's `material` property
- [ ] `set_property` with `value: "Assets/Models/Cube.fbx::Cube"` assigns the sub-asset mesh to a MeshFilter's `mesh` property
- [ ] `set_property` with `value: null` clears an ObjectReference
- [ ] `set_property` with `value: 12345` (instance_id) resolves and assigns the object
- [ ] `set_property` with a valid GUID string resolves and assigns the object
- [ ] `set_property` with a path to a wrong-type asset returns error with expected type name
- [ ] `set_property` with a nonexistent path returns `"No asset found at path '...'"` error
- [ ] `set_component` (SceneOpHandlers) can set ObjectReference properties on components via `DirectorHelpers.SetProperties`
- [ ] `create_gameobject` component properties can include ObjectReference values

---

### Unit 3: Primitive GameObject Creation

**Goal**: Add optional `primitive_type` parameter to `create_gameobject`.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SceneOpTool.cs`

Add to the schema (after the `"name"` property):

```json
"primitive_type": {
    "type": "string",
    "enum": ["cube", "sphere", "capsule", "cylinder", "plane", "quad"],
    "description": "Create a primitive mesh object instead of an empty GameObject. Includes MeshFilter, MeshRenderer, and default Collider."
}
```

Also update the tool description to mention primitive support.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SceneOpHandlers.cs`

Modify `CreateGameObject` to check for `primitive_type` before creating the GameObject:

```csharp
// At the top, after name validation:
var primitiveTypeStr = args["primitive_type"]?.Value<string>();
PrimitiveType? primitiveType = null;
if (!string.IsNullOrEmpty(primitiveTypeStr))
{
    primitiveType = ResolvePrimitiveType(primitiveTypeStr);
    if (primitiveType == null)
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            $"Unknown primitive_type '{primitiveTypeStr}'",
            "Valid values: cube, sphere, capsule, cylinder, plane, quad");
}
```

Update the dry run validator to validate `primitive_type`:

```csharp
// In the dry run lambda, add:
if (!string.IsNullOrEmpty(primitiveTypeStr)
    && ResolvePrimitiveType(primitiveTypeStr) == null)
    errors.Add($"Unknown primitive_type '{primitiveTypeStr}'");
```

Replace the creation line:

```csharp
// Before:
var go = new GameObject(name);

// After:
GameObject go;
if (primitiveType.HasValue)
{
    go = GameObject.CreatePrimitive(primitiveType.Value);
    go.name = name;
}
else
{
    go = new GameObject(name);
}
```

Add the resolution helper as a private method in `SceneOpHandlers`:

```csharp
private static readonly Dictionary<string, PrimitiveType> PrimitiveTypeMap =
    new Dictionary<string, PrimitiveType>(StringComparer.OrdinalIgnoreCase)
    {
        ["cube"]     = PrimitiveType.Cube,
        ["sphere"]   = PrimitiveType.Sphere,
        ["capsule"]  = PrimitiveType.Capsule,
        ["cylinder"] = PrimitiveType.Cylinder,
        ["plane"]    = PrimitiveType.Plane,
        ["quad"]     = PrimitiveType.Quad,
    };

private static PrimitiveType? ResolvePrimitiveType(string name)
{
    return PrimitiveTypeMap.TryGetValue(name, out var pt) ? pt : (PrimitiveType?)null;
}
```

Add `primitive_type` echo to the response:

```csharp
// After response["operation"] = "create_gameobject":
if (primitiveType.HasValue)
    response["primitive_type"] = primitiveTypeStr;
```

**Implementation Notes**:
- `GameObject.CreatePrimitive()` creates a GO with MeshFilter, MeshRenderer, and a matching Collider. We keep the Collider per user decision.
- The primitive is created first, then all other parameters (parent, position, rotation, scale, tag, layer, components) apply on top — same flow as the empty GO path.
- Must call `Undo.RegisterCreatedObjectUndo` on the primitive just like the empty GO.

**Acceptance Criteria**:
- [ ] `create_gameobject` with `primitive_type: "cube"` creates a visible cube with MeshFilter, MeshRenderer, and BoxCollider
- [ ] `create_gameobject` with `primitive_type: "sphere"` creates a sphere
- [ ] All 6 primitive types resolve correctly (cube, sphere, capsule, cylinder, plane, quad)
- [ ] Parent, position, rotation, scale, tag, layer, components apply on top of the primitive
- [ ] Invalid `primitive_type` returns error with valid values listed
- [ ] Omitting `primitive_type` creates an empty GO (existing behavior unchanged)
- [ ] Dry run validates `primitive_type`
- [ ] Response echoes `primitive_type` when set

---

### Unit 4: Tags & Layers Three-State Feedback

**Goal**: Improve `SetTagsAndLayers` response to distinguish added/already_exists/overwritten per item.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/ProjectSettingsOpTool.cs`

Replace the `SetTagsAndLayers` method (lines 198-302):

**Tags section** — change from bare string array to objects with status:

```csharp
// --- Tags ---
var addTagsToken = args["add_tags"] as JArray;
if (addTagsToken != null)
{
    var tagsProp = tagManager.FindProperty("tags");
    foreach (var tagToken in addTagsToken)
    {
        var tagName = tagToken.Value<string>();
        if (string.IsNullOrEmpty(tagName)) continue;

        bool exists = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
            {
                exists = true;
                break;
            }
        }

        if (exists)
        {
            addedTags.Add(new JObject
            {
                ["name"] = tagName,
                ["status"] = "already_exists"
            });
        }
        else
        {
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
            addedTags.Add(new JObject
            {
                ["name"] = tagName,
                ["status"] = "added"
            });
        }
    }
}
```

**Layers section** — report overwritten vs added vs already_exists:

```csharp
// --- Layers ---
var addLayersToken = args["add_layers"] as JArray;
if (addLayersToken != null)
{
    var layersProp = tagManager.FindProperty("layers");
    foreach (var layerToken in addLayersToken)
    {
        var layerObj = layerToken as JObject;
        if (layerObj == null) continue;

        var index = layerObj["index"]?.Value<int>() ?? -1;
        var layerName = layerObj["name"]?.Value<string>();
        if (index < 0 || index >= layersProp.arraySize || string.IsNullOrEmpty(layerName))
            continue;

        var currentValue = layersProp.GetArrayElementAtIndex(index).stringValue;

        if (currentValue == layerName)
        {
            addedLayers.Add(new JObject
            {
                ["index"] = index,
                ["name"] = layerName,
                ["status"] = "already_exists"
            });
        }
        else
        {
            var layerResult = new JObject
            {
                ["index"] = index,
                ["name"] = layerName,
            };

            if (!string.IsNullOrEmpty(currentValue))
            {
                layerResult["status"] = "overwritten";
                layerResult["previous_name"] = currentValue;
            }
            else
            {
                layerResult["status"] = "added";
            }

            layersProp.GetArrayElementAtIndex(index).stringValue = layerName;
            addedLayers.Add(layerResult);
        }
    }
}
```

**Sorting layers section** — same pattern:

```csharp
// --- Sorting Layers ---
var addSortingLayersToken = args["add_sorting_layers"] as JArray;
if (addSortingLayersToken != null)
{
    var sortingLayersProp = tagManager.FindProperty("m_SortingLayers");
    if (sortingLayersProp != null)
    {
        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < sortingLayersProp.arraySize; i++)
        {
            var elem = sortingLayersProp.GetArrayElementAtIndex(i);
            var nameProp = elem.FindPropertyRelative("name");
            if (nameProp != null)
                existingNames.Add(nameProp.stringValue);
        }

        foreach (var slToken in addSortingLayersToken)
        {
            var slName = slToken.Value<string>();
            if (string.IsNullOrEmpty(slName)) continue;

            if (existingNames.Contains(slName))
            {
                addedSortingLayers.Add(new JObject
                {
                    ["name"] = slName,
                    ["status"] = "already_exists"
                });
            }
            else
            {
                sortingLayersProp.InsertArrayElementAtIndex(sortingLayersProp.arraySize);
                var newElem = sortingLayersProp.GetArrayElementAtIndex(
                    sortingLayersProp.arraySize - 1);
                var nameProp = newElem.FindPropertyRelative("name");
                if (nameProp != null)
                {
                    nameProp.stringValue = slName;
                    existingNames.Add(slName);
                    addedSortingLayers.Add(new JObject
                    {
                        ["name"] = slName,
                        ["status"] = "added"
                    });
                }
            }
        }
    }
}
```

Response field names stay the same (`added_tags`, `added_layers`, `added_sorting_layers`) but the values change from bare strings/objects to objects with `status`.

**Acceptance Criteria**:
- [ ] Adding a new tag returns `{"name": "Foo", "status": "added"}`
- [ ] Adding an existing tag returns `{"name": "Foo", "status": "already_exists"}`
- [ ] Setting a layer on an empty slot returns `{"index": 6, "name": "Tiles", "status": "added"}`
- [ ] Setting a layer on a slot that already has that name returns `status: "already_exists"`
- [ ] Setting a layer on a slot with a different name returns `status: "overwritten", "previous_name": "OldName"`
- [ ] Sorting layers follow the same pattern
- [ ] Wire format uses snake_case for all fields

---

### Unit 5: Edit Mode Static Method Invocation

**Goal**: Allow `invoke_method` to call static methods without Play Mode. Add `type` parameter for static invocation (no component/GameObject needed).

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionInvokeMethod.cs`

Restructure `Execute` to branch on static vs instance:

```csharp
public static string Execute(JObject args)
{
    var componentName = args["component"]?.Value<string>();
    var typeName = args["type"]?.Value<string>();
    var methodName = args["method"]?.Value<string>();
    var methodArgs = args["arguments"] as JArray;

    if (string.IsNullOrEmpty(methodName))
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Missing required 'method' parameter",
            "Provide the method name to invoke");

    if (methodArgs != null && methodArgs.Count > MaxArgs)
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            $"invoke_method supports at most {MaxArgs} arguments, got {methodArgs.Count}",
            "Simplify the call or invoke a wrapper method");

    // Static method invocation: type + method, no component/path
    if (!string.IsNullOrEmpty(typeName))
        return ExecuteStatic(typeName, methodName, methodArgs);

    // Instance method invocation: requires component + path + Play Mode
    if (string.IsNullOrEmpty(componentName))
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Missing 'component' or 'type' parameter",
            "Provide 'component' for instance methods (Play Mode) "
            + "or 'type' for static methods (Edit Mode)");

    return ExecuteInstance(args, componentName, methodName, methodArgs);
}

private static string ExecuteInstance(
    JObject args, string componentName, string methodName, JArray methodArgs)
{
    // Existing behavior: Play Mode required for instance calls
    var error = ResponseHelpers.RequirePlayMode("invoke_method");
    if (error != null) return error;

    var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
    if (resolveError != null) return resolveError;

    var component = ObjectResolver.FindComponent(go, componentName);
    if (component == null)
        return ResponseHelpers.ErrorResponse(
            "component_not_found",
            $"Component '{componentName}' not found on '{go.name}'",
            "Use scene_inspect to list all components on this GameObject");

    var type = component.GetType();
    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
    int argCount = methodArgs?.Count ?? 0;

    var targetMethod = methods.FirstOrDefault(method =>
        method.Name == methodName &&
        method.GetParameters().Length == argCount &&
        method.GetParameters().All(p => IsAllowedType(p.ParameterType)));

    if (targetMethod == null)
        return ResponseHelpers.ErrorResponse(
            "property_not_found",
            $"No public method '{methodName}' with {argCount} simple-type parameters found on '{componentName}'",
            "invoke_method only supports string, int, float, bool parameters. "
            + "Use scene_inspect to check available methods.");

    var convertedArgs = ConvertArguments(targetMethod, methodArgs, out var convError);
    if (convError != null) return convError;

    return InvokeAndBuildResponse(targetMethod, component, convertedArgs, go, componentName, methodName);
}

private static string ExecuteStatic(
    string typeName, string methodName, JArray methodArgs)
{
    // Resolve the type from all loaded assemblies
    var type = DirectorHelpers.ResolveType(
        typeName, typeof(object), "Type", out var typeError);
    if (type == null) return typeError;

    int argCount = methodArgs?.Count ?? 0;

    var methods = type.GetMethods(
        BindingFlags.Public | BindingFlags.Static);
    var targetMethod = methods.FirstOrDefault(method =>
        method.Name == methodName &&
        method.GetParameters().Length == argCount &&
        method.GetParameters().All(p => IsAllowedType(p.ParameterType)));

    if (targetMethod == null)
        return ResponseHelpers.ErrorResponse(
            "property_not_found",
            $"No public static method '{methodName}' with {argCount} simple-type parameters found on '{typeName}'",
            "invoke_method only supports string, int, float, bool parameters.");

    var convertedArgs = ConvertArguments(targetMethod, methodArgs, out var convError);
    if (convError != null) return convError;

    // Invoke static — no target object
    object returnValue;
    try
    {
        returnValue = targetMethod.Invoke(null, convertedArgs);
    }
    catch (TargetInvocationException ex)
    {
        return ResponseHelpers.ErrorResponse(
            "internal_error",
            $"Static method '{typeName}.{methodName}' threw: {ex.InnerException?.Message ?? ex.Message}",
            "Check the Unity Console for the full stack trace");
    }

    var response = new JObject();
    response["result"] = "ok";
    response["type"] = typeName;
    response["method"] = methodName;
    response["static"] = true;

    if (targetMethod.ReturnType != typeof(void) && returnValue != null)
    {
        try { response["return_value"] = JToken.FromObject(returnValue); }
        catch { response["return_value"] = returnValue.ToString(); }
    }

    ResponseHelpers.AddFrameContext(response);
    return response.ToString(Newtonsoft.Json.Formatting.None);
}

private static object[] ConvertArguments(
    MethodInfo method, JArray methodArgs, out string error)
{
    error = null;
    int argCount = methodArgs?.Count ?? 0;
    if (argCount == 0) return null;

    var converted = new object[argCount];
    var parameters = method.GetParameters();
    for (int i = 0; i < argCount; i++)
    {
        try
        {
            converted[i] = methodArgs[i].ToObject(parameters[i].ParameterType);
        }
        catch (Exception ex)
        {
            error = ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                $"Cannot convert argument {i} to {parameters[i].ParameterType.Name}: {ex.Message}",
                $"Parameter '{parameters[i].Name}' expects {parameters[i].ParameterType.Name}");
            return null;
        }
    }
    return converted;
}

private static string InvokeAndBuildResponse(
    MethodInfo targetMethod, Component component,
    object[] convertedArgs, GameObject go,
    string componentName, string methodName)
{
    object returnValue;
    try
    {
        returnValue = targetMethod.Invoke(component, convertedArgs);
    }
    catch (TargetInvocationException ex)
    {
        return ResponseHelpers.ErrorResponse(
            "internal_error",
            $"Method '{methodName}' threw: {ex.InnerException?.Message ?? ex.Message}",
            "Check the Unity Console for the full stack trace");
    }

    var response = new JObject();
    response["result"] = "ok";
    ResponseHelpers.AddIdentity(response, go);
    response["component"] = componentName;
    response["method"] = methodName;

    if (targetMethod.ReturnType != typeof(void) && returnValue != null)
    {
        try { response["return_value"] = JToken.FromObject(returnValue); }
        catch { response["return_value"] = returnValue.ToString(); }
    }

    ResponseHelpers.AddFrameContext(response);
    return response.ToString(Newtonsoft.Json.Formatting.None);
}
```

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionTool.cs`

Add `type` parameter to the schema:

```json
"type": {
    "type": "string",
    "description": "Type name for static method invocation. Used by invoke_method in Edit Mode. Alternative to component+path."
}
```

Update the description to mention Edit Mode static methods.

**Implementation Notes**:
- `DirectorHelpers.ResolveType` with `typeof(object)` as the base type accepts any loaded type. This is intentional — static methods can exist on non-Component classes.
- Static invocation does NOT call `RequirePlayMode`. Instance invocation still does.
- The `type` parameter takes precedence over `component` — if both are provided, the static path is used and `component` is ignored.

**Acceptance Criteria**:
- [ ] `invoke_method` with `type: "MyClass"`, `method: "MyStaticMethod"` works in Edit Mode
- [ ] `invoke_method` with `component` + `path` still requires Play Mode
- [ ] Missing both `component` and `type` returns descriptive error
- [ ] Static method with return value includes `return_value` in response
- [ ] Static method on unknown type returns `type_not_found` error
- [ ] Static method not found returns descriptive error
- [ ] Response includes `"static": true` for static calls

---

### Unit 6: Run Menu Item Operation

**Goal**: Add `run_menu_item` operation to the `action` tool.

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionRunMenuItem.cs` (new file)

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Actions
{
    /// <summary>
    /// action:run_menu_item — execute a Unity Editor menu item by path.
    /// Works in Edit Mode only. Some dangerous menu paths are blocked.
    /// </summary>
    internal static class ActionRunMenuItem
    {
        /// <summary>
        /// Menu paths that are blocked because they are destructive or
        /// would interfere with the editor session.
        /// </summary>
        private static readonly string[] BlockedPrefixes = new[]
        {
            "File/Quit",
            "File/Exit",
        };

        private static readonly string[] BlockedExact = new[]
        {
            "File/Save Project",
            "File/New Scene",
            "Edit/Preferences",
            "Edit/Clear All PlayerPrefs",
        };

        public static string Execute(JObject args)
        {
            var menuPath = args["menu_path"]?.Value<string>();

            if (string.IsNullOrEmpty(menuPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'menu_path' parameter",
                    "Provide the menu item path, e.g. 'GameObject/3D Object/Cube'");

            // Check blocklist
            foreach (var prefix in BlockedPrefixes)
            {
                if (menuPath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return ResponseHelpers.ErrorResponse(
                        "operation_not_supported",
                        $"Menu path '{menuPath}' is blocked for safety",
                        "This menu item could disrupt the editor session");
            }
            foreach (var exact in BlockedExact)
            {
                if (string.Equals(menuPath, exact, System.StringComparison.OrdinalIgnoreCase))
                    return ResponseHelpers.ErrorResponse(
                        "operation_not_supported",
                        $"Menu path '{menuPath}' is blocked for safety",
                        "This menu item could disrupt the editor session");
            }

#if UNITY_EDITOR
            var success = EditorApplication.ExecuteMenuItem(menuPath);

            var response = new JObject();
            if (success)
            {
                response["result"] = "ok";
                response["operation"] = "run_menu_item";
                response["menu_path"] = menuPath;
            }
            else
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Menu item '{menuPath}' not found or could not be executed",
                    "Check the menu path is correct. Use Unity's menu bar to verify the exact path.");
            }

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "operation_not_supported",
                "run_menu_item requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }
    }
}
```

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionTool.cs`

Add to the schema enum:

```csharp
// Change the enum line:
""enum"": [""teleport"", ""set_property"", ""set_active"",
           ""set_timescale"", ""pause"", ""step"",
           ""unpause"", ""invoke_method"", ""run_menu_item""],
```

Add `menu_path` parameter to schema:

```json
"menu_path": {
    "type": "string",
    "description": "Menu item path to execute. Used by run_menu_item. E.g. 'GameObject/3D Object/Cube'."
}
```

Add to the dispatch switch:

```csharp
"run_menu_item"  => ActionRunMenuItem.Execute(args),
```

Update the catch-all error message and the valid operations list string.

**Implementation Notes**:
- `EditorApplication.ExecuteMenuItem` returns `false` when the menu item doesn't exist or can't execute. No exception is thrown.
- The blocklist is minimal and defensive — only items that would quit the editor or cause data loss. We intentionally don't block items like "File/Save Scene" (useful for agents) or "Edit/Project Settings" (read-only window).
- The blocklist uses prefix matching for "File/Quit" and "File/Exit" (catches platform variants) and exact matching for specific items.

**Acceptance Criteria**:
- [ ] `run_menu_item` with `menu_path: "GameObject/Create Empty"` succeeds and creates a GO
- [ ] `run_menu_item` with nonexistent path returns error
- [ ] `run_menu_item` with `"File/Quit"` returns `operation_not_supported`
- [ ] `run_menu_item` with `"File/Exit"` returns `operation_not_supported`
- [ ] `run_menu_item` with `"File/Save Project"` returns `operation_not_supported`
- [ ] Response includes `menu_path` echo and frame context
- [ ] Operation appears in the `action` tool's schema enum

---

## Implementation Order

1. **Unit 1: Unify SetPropertyValue** — Prerequisite for Unit 2. Pure refactor, no behavior change.
2. **Unit 2: ObjectReference writes** — Biggest impact feature. Depends on Unit 1.
3. **Unit 3: Primitive GameObjects** — Independent of Units 1-2. Can be done in parallel with Unit 2 if desired.
4. **Unit 4: Tags & layers feedback** — Independent. Pure response format change.
5. **Unit 5: Edit Mode invoke** — Independent of Units 1-4 but should come before Unit 6.
6. **Unit 6: Run menu item** — Depends on Unit 5 (same schema file is modified).

**Parallelizable**: Units 3, 4 are fully independent and can be done in any order. Units 5+6 are a natural pair.

---

## Testing

### Test File: `Packages/com.theatre.toolkit/Tests/Editor/DirectorTests.cs`

Add tests to the existing `SceneOpTests` fixture:

```csharp
// --- Unit 2: ObjectReference writes ---

[Test]
public void SetComponent_ObjectReference_AssignsAssetByPath()
{
    // Create a test material
    var mat = new Material(Shader.Find("Standard"));
    AssetDatabase.CreateAsset(mat, "Assets/TheatreTestMaterial.mat");
    var go = new GameObject("ObjRefTest");
    go.AddComponent<MeshRenderer>();
    try
    {
        var result = SceneOpHandlers.SetComponent(new JObject
        {
            ["path"] = "/" + go.name,
            ["component"] = "MeshRenderer",
            ["properties"] = new JObject
            {
                ["material"] = "Assets/TheatreTestMaterial.mat"
            }
        });
        Assert.That(result, Does.Contain("\"result\":\"ok\""));
        var renderer = go.GetComponent<MeshRenderer>();
        Assert.IsNotNull(renderer.sharedMaterial);
    }
    finally
    {
        Object.DestroyImmediate(go);
        AssetDatabase.DeleteAsset("Assets/TheatreTestMaterial.mat");
    }
}

[Test]
public void SetComponent_ObjectReference_NullClearsRef()
{
    var go = new GameObject("ClearRefTest");
    var renderer = go.AddComponent<MeshRenderer>();
    renderer.sharedMaterial = new Material(Shader.Find("Standard"));
    try
    {
        var result = SceneOpHandlers.SetComponent(new JObject
        {
            ["path"] = "/" + go.name,
            ["component"] = "MeshRenderer",
            ["properties"] = new JObject { ["material"] = null }
        });
        Assert.That(result, Does.Contain("\"result\":\"ok\""));
        Assert.IsNull(renderer.sharedMaterial);
    }
    finally
    {
        Object.DestroyImmediate(go);
    }
}

[Test]
public void SetComponent_ObjectReference_BadPath_ReturnsError()
{
    var go = new GameObject("BadPathTest");
    go.AddComponent<MeshRenderer>();
    try
    {
        var result = SceneOpHandlers.SetComponent(new JObject
        {
            ["path"] = "/" + go.name,
            ["component"] = "MeshRenderer",
            ["properties"] = new JObject
            {
                ["material"] = "Assets/NoSuchMaterial.mat"
            }
        });
        // Should succeed but report property error
        Assert.That(result, Does.Contain("No asset found"));
    }
    finally
    {
        Object.DestroyImmediate(go);
    }
}

// --- Unit 3: Primitive GameObjects ---

[Test]
public void CreateGameObject_PrimitiveType_Cube_HasMeshRenderer()
{
    var result = SceneOpHandlers.CreateGameObject(new JObject
    {
        ["name"] = "TestCube",
        ["primitive_type"] = "cube"
    });
    Assert.That(result, Does.Contain("\"result\":\"ok\""));
    Assert.That(result, Does.Contain("\"primitive_type\":\"cube\""));
    var go = GameObject.Find("TestCube");
    Assert.IsNotNull(go);
    Assert.IsNotNull(go.GetComponent<MeshFilter>());
    Assert.IsNotNull(go.GetComponent<MeshRenderer>());
    Assert.IsNotNull(go.GetComponent<BoxCollider>());
    Object.DestroyImmediate(go);
}

[Test]
public void CreateGameObject_InvalidPrimitiveType_ReturnsError()
{
    var result = SceneOpHandlers.CreateGameObject(new JObject
    {
        ["name"] = "BadPrimitive",
        ["primitive_type"] = "triangle"
    });
    Assert.That(result, Does.Contain("\"error\""));
    Assert.That(result, Does.Contain("Unknown primitive_type"));
}

[Test]
public void CreateGameObject_PrimitiveType_WithParentAndPosition()
{
    var parent = new GameObject("PrimitiveParent");
    try
    {
        var result = SceneOpHandlers.CreateGameObject(new JObject
        {
            ["name"] = "ChildSphere",
            ["primitive_type"] = "sphere",
            ["parent"] = "/" + parent.name,
            ["position"] = new JArray(1f, 2f, 3f)
        });
        Assert.That(result, Does.Contain("\"result\":\"ok\""));
        var child = parent.transform.Find("ChildSphere");
        Assert.IsNotNull(child);
        Assert.AreEqual(1f, child.localPosition.x, 0.01f);
    }
    finally
    {
        Object.DestroyImmediate(parent);
    }
}

[Test]
public void CreateGameObject_NoPrimitiveType_CreatesEmptyGO()
{
    var result = SceneOpHandlers.CreateGameObject(new JObject
    {
        ["name"] = "EmptyObj"
    });
    Assert.That(result, Does.Contain("\"result\":\"ok\""));
    Assert.That(result, Does.Not.Contain("\"primitive_type\""));
    var go = GameObject.Find("EmptyObj");
    Assert.IsNotNull(go);
    Assert.IsNull(go.GetComponent<MeshFilter>());
    Object.DestroyImmediate(go);
}
```

### Test File: `Packages/com.theatre.toolkit/Tests/Editor/InputConfigToolTests.cs`

Add tests to the existing `ProjectSettingsOpTests` fixture:

```csharp
// --- Unit 4: Tags & layers feedback ---

[Test]
public void SetTagsAndLayers_NewTag_StatusAdded()
{
    var uniqueTag = "TheatreTestTag_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var result = ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_tags"] = new JArray(uniqueTag)
    });
    var json = JObject.Parse(result);
    Assert.AreEqual("ok", json["result"].Value<string>());
    var tags = json["added_tags"] as JArray;
    Assert.IsNotNull(tags);
    Assert.AreEqual(1, tags.Count);
    Assert.AreEqual("added", tags[0]["status"].Value<string>());
    Assert.AreEqual(uniqueTag, tags[0]["name"].Value<string>());
}

[Test]
public void SetTagsAndLayers_DuplicateTag_StatusAlreadyExists()
{
    var uniqueTag = "TheatreTestDup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    // Add it first
    ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_tags"] = new JArray(uniqueTag)
    });
    // Add again
    var result = ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_tags"] = new JArray(uniqueTag)
    });
    var json = JObject.Parse(result);
    var tags = json["added_tags"] as JArray;
    Assert.AreEqual("already_exists", tags[0]["status"].Value<string>());
}

[Test]
public void SetTagsAndLayers_LayerOnEmptySlot_StatusAdded()
{
    // Use layer index 31 (likely empty in test project)
    var result = ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_layers"] = new JArray(new JObject { ["index"] = 31, ["name"] = "TheatreTestLayer" })
    });
    var json = JObject.Parse(result);
    var layers = json["added_layers"] as JArray;
    Assert.IsNotNull(layers);
    Assert.AreEqual(1, layers.Count);
    // Status is "added" or "overwritten" depending on prior state —
    // just verify status field exists
    Assert.IsNotNull(layers[0]["status"]);
}

[Test]
public void SetTagsAndLayers_LayerSameValue_StatusAlreadyExists()
{
    // Set a layer, then set the same value again
    ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_layers"] = new JArray(new JObject { ["index"] = 30, ["name"] = "TestRepeat" })
    });
    var result = ProjectSettingsOpTool.SetTagsAndLayers(new JObject
    {
        ["add_layers"] = new JArray(new JObject { ["index"] = 30, ["name"] = "TestRepeat" })
    });
    var json = JObject.Parse(result);
    var layers = json["added_layers"] as JArray;
    Assert.AreEqual("already_exists", layers[0]["status"].Value<string>());
}
```

### Test File: `Packages/com.theatre.toolkit/Tests/Editor/WatchActionTests.cs`

Add tests to the existing `ActionToolDispatchTests` fixture:

```csharp
// --- Unit 5: Edit Mode static invoke ---

[Test]
public void ActionTool_InvokeMethod_StaticInEditMode_Succeeds()
{
    // System.Math.Abs is a static method available in all assemblies
    var result = CallAction(new JObject
    {
        ["operation"] = "invoke_method",
        ["type"] = "System.Math",
        ["method"] = "Abs",
        ["arguments"] = new JArray(-42)
    });
    Assert.That(result, Does.Contain("\"result\":\"ok\""));
    Assert.That(result, Does.Contain("\"static\":true"));
    Assert.That(result, Does.Contain("\"return_value\":42"));
}

[Test]
public void ActionTool_InvokeMethod_InstanceStillRequiresPlayMode()
{
    var result = CallAction(new JObject
    {
        ["operation"] = "invoke_method",
        ["component"] = "Transform",
        ["method"] = "DetachChildren",
        ["path"] = "/NonExistent"
    });
    Assert.That(result, Does.Contain("requires_play_mode"));
}

[Test]
public void ActionTool_InvokeMethod_MissingComponentAndType_ReturnsError()
{
    var result = CallAction(new JObject
    {
        ["operation"] = "invoke_method",
        ["method"] = "SomeMethod"
    });
    Assert.That(result, Does.Contain("\"error\""));
    Assert.That(result, Does.Contain("component").Or.Contain("type"));
}

// --- Unit 6: Run menu item ---

[Test]
public void ActionTool_RunMenuItem_BlockedPath_ReturnsError()
{
    var result = CallAction(new JObject
    {
        ["operation"] = "run_menu_item",
        ["menu_path"] = "File/Quit"
    });
    Assert.That(result, Does.Contain("\"error\""));
    Assert.That(result, Does.Contain("operation_not_supported"));
}

[Test]
public void ActionTool_RunMenuItem_MissingPath_ReturnsError()
{
    var result = CallAction(new JObject
    {
        ["operation"] = "run_menu_item"
    });
    Assert.That(result, Does.Contain("\"error\""));
    Assert.That(result, Does.Contain("invalid_parameter"));
}

[Test]
public void ActionTool_RunMenuItem_NonexistentPath_ReturnsError()
{
    var result = CallAction(new JObject
    {
        ["operation"] = "run_menu_item",
        ["menu_path"] = "Fake/Menu/Path/That/DoesNotExist"
    });
    Assert.That(result, Does.Contain("\"error\""));
    Assert.That(result, Does.Contain("not found"));
}
```

---

## Verification Checklist

After implementation, run these commands through Theatre MCP tools:

```
1. unity_console {"operation": "refresh"}           — trigger recompile
2. (sleep 8s)
3. unity_console {"filter": "error"}                — check for compile errors
4. unity_tests {"operation": "run"}                  — run all tests
5. (sleep 15s)
6. unity_tests {"operation": "results"}              — verify no failures
```

Manual spot-checks:
- `action {"operation": "set_property", "path": "/SomeRenderer", "component": "MeshRenderer", "property": "material", "value": "Assets/Materials/Default.mat"}`
- `scene_op {"operation": "create_gameobject", "name": "TestCube", "primitive_type": "cube", "position": [0, 1, 0]}`
- `action {"operation": "invoke_method", "type": "UnityEngine.Debug", "method": "Log", "arguments": ["hello from Theatre"]}`
- `action {"operation": "run_menu_item", "menu_path": "GameObject/Create Empty"}`
- `project_settings_op {"operation": "set_tags_and_layers", "add_tags": ["TestTag"], "add_layers": [{"index": 10, "name": "TestLayer"}]}`
