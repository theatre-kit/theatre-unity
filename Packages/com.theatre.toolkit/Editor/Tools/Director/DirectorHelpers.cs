using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Theatre;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Shared utilities for Director tool handlers.
    /// Provides type resolution, asset path validation, property setting,
    /// dry-run support, and undo group management.
    /// </summary>
    internal static class DirectorHelpers
    {
        /// <summary>
        /// Resolve a type by name, filtering to those assignable from <paramref name="baseType"/>.
        /// Searches all loaded assemblies for an exact fully-qualified match or simple Name match.
        /// Returns null and sets error on failure or ambiguity.
        /// </summary>
        public static Type ResolveType(string typeName, Type baseType, string label, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(typeName))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"{label} type name must not be empty",
                    $"Provide a {label} type name such as 'BoxCollider' or 'Rigidbody'");
                return null;
            }

            var matches = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Try exact fully-qualified name first
                var exact = assembly.GetType(typeName);
                if (exact != null && baseType.IsAssignableFrom(exact))
                {
                    if (!matches.Contains(exact))
                        matches.Add(exact);
                    continue;
                }

                // Search by simple name
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName && baseType.IsAssignableFrom(type))
                        {
                            if (!matches.Contains(type))
                                matches.Add(type);
                        }
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // Some assemblies cannot enumerate types — skip them
                }
            }

            if (matches.Count == 0)
            {
                error = ResponseHelpers.ErrorResponse(
                    "type_not_found",
                    $"{label} type '{typeName}' not found in any loaded assembly",
                    $"Use the fully-qualified name (e.g., 'UnityEngine.BoxCollider'). Use scene_inspect on a GameObject with this component to verify the type name.");
                return null;
            }

            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(t => t.FullName));
                error = ResponseHelpers.ErrorResponse(
                    "type_ambiguous",
                    $"{label} type name '{typeName}' is ambiguous. Matches: {names}",
                    "Use the fully-qualified type name to disambiguate");
                return null;
            }

            return matches[0];
        }

        /// <summary>
        /// Resolve a Component type by name.
        /// Searches all loaded assemblies for an exact match or Name match.
        /// Returns null and sets error on failure or ambiguity.
        /// </summary>
        public static Type ResolveComponentType(string typeName, out string error)
            => ResolveType(typeName, typeof(Component), "Component", out error);

        /// <summary>
        /// Resolve a ScriptableObject type by name.
        /// Same logic as ResolveComponentType but filters on ScriptableObject inheritance.
        /// Returns null and sets error on failure or ambiguity.
        /// </summary>
        public static Type ResolveScriptableObjectType(string typeName, out string error)
            => ResolveType(typeName, typeof(ScriptableObject), "ScriptableObject", out error);

        /// <summary>
        /// Validate an asset path.
        /// Returns an error JSON string on failure, or null if valid.
        /// </summary>
        public static string ValidateAssetPath(string path, string requiredExtension = null)
        {
            if (string.IsNullOrEmpty(path))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Asset path must not be empty",
                    "Provide a path starting with 'Assets/' or 'Packages/'");

            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Asset path '{path}' must start with 'Assets/' or 'Packages/'",
                    "Example: 'Assets/Scenes/MyScene.unity'");

            if (requiredExtension != null &&
                !path.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Asset path '{path}' must end with '{requiredExtension}'",
                    $"Example: 'Assets/Scenes/MyScene{requiredExtension}'");

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Check if an asset already exists at the given path.
        /// Returns an error JSON string if it exists and overwrite is not allowed,
        /// or null if OK to proceed.
        /// </summary>
        public static string CheckAssetConflict(string path, bool allowOverwrite = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing != null && !allowOverwrite)
                return ResponseHelpers.ErrorResponse(
                    "asset_exists",
                    $"An asset already exists at '{path}'",
                    "Choose a different path or set 'overwrite' to true");
            return null;
        }

        /// <summary>
        /// Find a SerializedProperty using the 4-step name fallback
        /// (exact, m_PascalCase, PascalCase, m_original, m_Plurals, Plurals).
        /// </summary>
        public static SerializedProperty FindPropertyFuzzy(
            SerializedObject so, string snakeCaseName)
        {
            foreach (var candidate in StringUtils.GetPropertyNameCandidates(snakeCaseName))
            {
                var prop = so.FindProperty(candidate);
                if (prop != null) return prop;
            }
            return null;
        }

        /// <summary>
        /// List visible serialized property names on a Unity Object, returned
        /// as Theatre snake_case names. Skips m_Script. Max <paramref name="limit"/> entries.
        /// </summary>
        /// <summary>
        /// List visible serialized property names on a Unity Object, returned
        /// as Theatre snake_case names. Skips m_Script.
        /// When <paramref name="attemptedName"/> is provided, results are sorted
        /// by relevance (substring overlap) so the closest match appears first.
        /// </summary>
        public static List<string> ListPropertyNames(
            SerializedObject so, string attemptedName = null, int limit = 20)
        {
            var names = new List<string>();
            var prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                var rawName = prop.name;
                if (rawName.StartsWith("m_") && rawName.Length > 2)
                    rawName = rawName.Substring(2);
                var snakeName = Tools.Scene.PropertySerializer.ToSnakeCase(rawName);
                names.Add(snakeName);
            }

            if (!string.IsNullOrEmpty(attemptedName))
            {
                var query = attemptedName.ToLowerInvariant()
                    .Replace("_", "");
                names.Sort((a, b) =>
                    ScoreMatch(b, query).CompareTo(ScoreMatch(a, query)));
            }

            if (names.Count > limit)
                names.RemoveRange(limit, names.Count - limit);
            return names;
        }

        private static int ScoreMatch(string propertyName, string query)
        {
            var normalized = propertyName.ToLowerInvariant()
                .Replace("_", "");
            // Exact match
            if (normalized == query) return 100;
            // One contains the other
            if (normalized.Contains(query)) return 80;
            if (query.Contains(normalized)) return 70;
            // Share a common substring of 3+ chars
            for (int len = Math.Min(normalized.Length, query.Length);
                 len >= 3; len--)
            {
                for (int i = 0; i <= query.Length - len; i++)
                {
                    if (normalized.Contains(query.Substring(i, len)))
                        return 40 + len;
                }
            }
            return 0;
        }

        /// <summary>
        /// Set multiple fields on any UnityEngine.Object via SerializedObject.
        /// Works for Components, ScriptableObjects, or any serialized asset.
        /// Uses the same 4-step property name fallback as ActionSetProperty.
        /// Returns the count of successfully set fields and a list of per-field errors.
        /// </summary>
        public static (int successCount, List<string> errors) SetFields(
            UnityEngine.Object target, JObject fields)
        {
            int successCount = 0;
            var errors = new List<string>();

            if (fields == null || !fields.HasValues)
                return (0, errors);

            var so = new SerializedObject(target);

            foreach (var prop in fields.Properties())
            {
                var propName = prop.Name;
                var value = prop.Value;

                // 4-step fallback matching ActionSetProperty
                var sp = FindPropertyFuzzy(so, propName);

                if (sp == null)
                {
                    var available = ListPropertyNames(so, propName);
                    var availStr = available.Count > 0
                        ? $" Available: {string.Join(", ", available)}"
                        : "";
                    errors.Add($"Property '{propName}' not found on "
                        + $"{target.GetType().Name}.{availStr}");
                    continue;
                }

                if (!SetPropertyValue(sp, value, out var setError))
                {
                    errors.Add($"Cannot set '{propName}': {setError}");
                    continue;
                }

                successCount++;
            }

            so.ApplyModifiedProperties();
            return (successCount, errors);
        }

        /// <summary>
        /// Set multiple serialized properties on a component using a JObject map.
        /// Uses the same 4-step property name fallback as ActionSetProperty.
        /// Returns the count of successfully set properties and a list of per-property errors.
        /// Delegates to SetFields for implementation.
        /// </summary>
        public static (int successCount, List<string> errors) SetProperties(
            Component component, JObject properties)
        {
            return SetFields(component, properties);
        }

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

        /// <summary>
        /// Set a single SerializedProperty value from a JToken.
        /// Supports Integer, Float, Boolean, String, Vector2/3/4, Color,
        /// Quaternion, Enum, and ObjectReference.
        /// Returns true on success; on failure, sets error and returns false.
        /// </summary>
        internal static bool SetPropertyValue(
            SerializedProperty prop, JToken value, out string error)
        {
            error = null;
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.ToObject<int>();
                        return true;

                    case SerializedPropertyType.Float:
                        prop.floatValue = value.ToObject<float>();
                        return true;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.ToObject<bool>();
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value.ToObject<string>();
                        return true;

                    case SerializedPropertyType.Vector2:
                        if (value is JArray v2 && v2.Count >= 2)
                        {
                            prop.vector2Value = new Vector2(
                                v2[0].Value<float>(), v2[1].Value<float>());
                            return true;
                        }
                        error = "Vector2 requires [x, y] array";
                        return false;

                    case SerializedPropertyType.Vector3:
                        if (value is JArray v3 && v3.Count >= 3)
                        {
                            prop.vector3Value = new Vector3(
                                v3[0].Value<float>(),
                                v3[1].Value<float>(),
                                v3[2].Value<float>());
                            return true;
                        }
                        error = "Vector3 requires [x, y, z] array";
                        return false;

                    case SerializedPropertyType.Vector4:
                        if (value is JArray v4 && v4.Count >= 4)
                        {
                            prop.vector4Value = new Vector4(
                                v4[0].Value<float>(),
                                v4[1].Value<float>(),
                                v4[2].Value<float>(),
                                v4[3].Value<float>());
                            return true;
                        }
                        error = "Vector4 requires [x, y, z, w] array";
                        return false;

                    case SerializedPropertyType.Color:
                        if (value is JArray c && c.Count >= 4)
                        {
                            prop.colorValue = new Color(
                                c[0].Value<float>(), c[1].Value<float>(),
                                c[2].Value<float>(), c[3].Value<float>());
                            return true;
                        }
                        error = "Color requires [r, g, b, a] array";
                        return false;

                    case SerializedPropertyType.Quaternion:
                        if (value is JArray q && q.Count >= 4)
                        {
                            prop.quaternionValue = new Quaternion(
                                q[0].Value<float>(), q[1].Value<float>(),
                                q[2].Value<float>(), q[3].Value<float>());
                            return true;
                        }
                        error = "Quaternion requires [x, y, z, w] array";
                        return false;

                    case SerializedPropertyType.Enum:
                        var enumStr = value.ToObject<string>();
                        for (int i = 0; i < prop.enumDisplayNames.Length; i++)
                        {
                            if (string.Equals(prop.enumDisplayNames[i],
                                enumStr, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = i;
                                return true;
                            }
                        }
                        error = $"Unknown enum value '{enumStr}'. "
                              + $"Valid: {string.Join(", ", prop.enumDisplayNames)}";
                        return false;

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

                    default:
                        // Handle arrays of ObjectReference (e.g. m_Materials on MeshRenderer)
                        if (prop.isArray && prop.arraySize > 0)
                        {
                            var elem = prop.GetArrayElementAtIndex(0);
                            if (elem.propertyType == SerializedPropertyType.ObjectReference)
                                return SetObjectReferenceArray(prop, value, out error);
                        }
                        error = $"Unsupported property type: {prop.propertyType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Set an array-of-ObjectReference property from a JToken.
        /// Accepts a single value (sets element 0) or a JArray of values.
        /// Each element is resolved via ResolveObjectReference.
        /// </summary>
        private static bool SetObjectReferenceArray(
            SerializedProperty arrayProp, JToken value, out string error)
        {
            error = null;

            // Single value → set element [0], resize array to 1 if needed
            if (value.Type != JTokenType.Array)
            {
                if (arrayProp.arraySize == 0)
                    arrayProp.InsertArrayElementAtIndex(0);
                // Shrink to 1 element for single-value assignment
                while (arrayProp.arraySize > 1)
                    arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);

                var elem = arrayProp.GetArrayElementAtIndex(0);
                return SetPropertyValue(elem, value, out error);
            }

            // JArray → set each element
            var arr = (JArray)value;
            // Resize array to match
            while (arrayProp.arraySize < arr.Count)
                arrayProp.InsertArrayElementAtIndex(arrayProp.arraySize);
            while (arrayProp.arraySize > arr.Count)
                arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);

            for (int i = 0; i < arr.Count; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                if (!SetPropertyValue(elem, arr[i], out error))
                {
                    error = $"Element [{i}]: {error}";
                    return false;
                }
            }

            return true;
        }

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
                  + $"integer (instance_id), or null. Got {value.Type}";
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
            var names = allAssets
                .Where(a => a != null && !string.IsNullOrEmpty(a.name))
                .Select(a => a.name).ToList();

            error = $"Sub-asset '{subAssetName}' not found in '{mainPath}'. "
                  + $"Available: {string.Join(", ", names)}";
            return null;
        }

        /// <summary>
        /// Get the expected System.Type for an ObjectReference SerializedProperty.
        /// Uses reflection on the target object's field declaration.
        /// Returns null if the type cannot be determined (fallback: no type check).
        /// </summary>
        private static Type GetFieldType(SerializedProperty prop)
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
            Type currentType = targetType;
            foreach (var part in fieldName.Split('.'))
            {
                var field = currentType.GetField(part,
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic);
                if (field == null) return null;
                currentType = field.FieldType;
            }

            // Unwrap arrays/lists to element type
            if (currentType.IsArray)
                return currentType.GetElementType();
            if (currentType.IsGenericType
                && currentType.GetGenericTypeDefinition() == typeof(List<>))
                return currentType.GetGenericArguments()[0];

            return currentType;
        }

        /// <summary>
        /// Ensure that the parent directory of the given asset path exists,
        /// creating intermediate folders as needed.
        /// Does nothing if the parent folder already exists.
        /// </summary>
        public static void EnsureParentDirectory(string assetPath)
        {
            var lastSlash = assetPath.LastIndexOf('/');
            if (lastSlash <= 0) return;

            var parentPath = assetPath.Substring(0, lastSlash);
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                var grandparentSlash = parentPath.LastIndexOf('/');
                if (grandparentSlash >= 0)
                {
                    var grandparent = parentPath.Substring(0, grandparentSlash);
                    var folderName = parentPath.Substring(grandparentSlash + 1);
                    EnsureParentDirectory(parentPath);
                    if (!AssetDatabase.IsValidFolder(parentPath))
                        AssetDatabase.CreateFolder(grandparent, folderName);
                }
            }
        }

        /// <summary>
        /// Parse asset_path from args, validate it, and load the asset.
        /// Returns null on success (asset and path written to out params),
        /// or an error response string on failure.
        /// </summary>
        public static string LoadAsset<T>(
            JObject args, out T asset, out string assetPath,
            string requiredExtension = null,
            string pathParam = "asset_path") where T : UnityEngine.Object
        {
            asset = null;
            assetPath = args[pathParam]?.Value<string>();
            var pathError = ValidateAssetPath(assetPath, requiredExtension);
            if (pathError != null) return pathError;

            asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"{typeof(T).Name} not found at '{assetPath}'",
                    $"Verify the path points to a {typeof(T).Name} under Assets/ or Packages/. Paths are case-sensitive and must include the file extension.");

            return null;
        }

        /// <summary>
        /// Begin an undo group for a named operation.
        /// Returns the group index for use with EndUndoGroup.
        /// </summary>
        public static int BeginUndoGroup(string operationName)
        {
            Undo.SetCurrentGroupName($"Theatre: {operationName}");
            return Undo.GetCurrentGroup();
        }

        /// <summary>
        /// Collapse all undo operations since BeginUndoGroup into a single entry.
        /// </summary>
        public static void EndUndoGroup(int groupIndex)
        {
            Undo.CollapseUndoOperations(groupIndex);
        }
#endif

        /// <summary>
        /// If dry_run is true in args, call the validator and return the dry-run response.
        /// Returns null if dry_run is not set (caller should proceed with real operation).
        /// </summary>
        public static string CheckDryRun(JObject args, Func<(bool wouldSucceed, List<string> errors)> validator)
        {
            if (args["dry_run"]?.Value<bool>() != true)
                return null;

            var (wouldSucceed, errors) = validator();

            var response = new JObject();
            response["dry_run"] = true;
            response["would_succeed"] = wouldSucceed;

            var errorArray = new JArray();
            if (errors != null)
            {
                foreach (var e in errors)
                    errorArray.Add(e);
            }
            response["errors"] = errorArray;

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Convert a snake_case string to PascalCase.
        /// E.g. "is_kinematic" -> "IsKinematic".
        /// Delegates to <see cref="StringUtils.ToPascalCase"/>.
        /// </summary>
        public static string ToPascalCase(string snakeCase)
            => StringUtils.ToPascalCase(snakeCase);
    }
}
