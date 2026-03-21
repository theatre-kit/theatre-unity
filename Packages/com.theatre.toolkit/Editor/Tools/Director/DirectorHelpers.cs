using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Theatre.Stage;
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
        /// Resolve a Component type by name.
        /// Searches all loaded assemblies for an exact match or Name match.
        /// Returns null and sets error on failure or ambiguity.
        /// </summary>
        public static Type ResolveComponentType(string typeName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(typeName))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Component type name must not be empty",
                    "Provide a component type name such as 'BoxCollider' or 'Rigidbody'");
                return null;
            }

            var matches = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Try exact fully-qualified name first
                var exact = assembly.GetType(typeName);
                if (exact != null && typeof(Component).IsAssignableFrom(exact))
                {
                    matches.Add(exact);
                    continue;
                }

                // Search by simple name
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName && typeof(Component).IsAssignableFrom(type))
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
                    $"Component type '{typeName}' not found in any loaded assembly",
                    "Check the type name is correct. Use fully-qualified name (e.g. 'UnityEngine.BoxCollider') to disambiguate.");
                return null;
            }

            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(t => t.FullName));
                error = ResponseHelpers.ErrorResponse(
                    "type_ambiguous",
                    $"Component type name '{typeName}' is ambiguous. Matches: {names}",
                    "Use the fully-qualified type name to disambiguate");
                return null;
            }

            return matches[0];
        }

        /// <summary>
        /// Resolve a ScriptableObject type by name.
        /// Same logic as ResolveComponentType but filters on ScriptableObject inheritance.
        /// Returns null and sets error on failure or ambiguity.
        /// </summary>
        public static Type ResolveScriptableObjectType(string typeName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(typeName))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "ScriptableObject type name must not be empty",
                    "Provide a ScriptableObject type name such as 'MyConfig' or 'UnityEngine.ScriptableObject'");
                return null;
            }

            var matches = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Try exact fully-qualified name first
                var exact = assembly.GetType(typeName);
                if (exact != null && typeof(ScriptableObject).IsAssignableFrom(exact))
                {
                    matches.Add(exact);
                    continue;
                }

                // Search by simple name
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName && typeof(ScriptableObject).IsAssignableFrom(type))
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
                    $"ScriptableObject type '{typeName}' not found in any loaded assembly",
                    "Check the type name is correct. Use fully-qualified name to disambiguate.");
                return null;
            }

            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(t => t.FullName));
                error = ResponseHelpers.ErrorResponse(
                    "type_ambiguous",
                    $"ScriptableObject type name '{typeName}' is ambiguous. Matches: {names}",
                    "Use the fully-qualified type name to disambiguate");
                return null;
            }

            return matches[0];
        }

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
                var sp = so.FindProperty(propName);
                if (sp == null)
                    sp = so.FindProperty("m_" + ToPascalCase(propName));
                if (sp == null)
                    sp = so.FindProperty(ToPascalCase(propName));
                if (sp == null)
                    sp = so.FindProperty("m_" + propName);

                if (sp == null)
                {
                    errors.Add($"Property '{propName}' not found on {target.GetType().Name}");
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

        private static bool SetPropertyValue(
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

                    default:
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
        /// </summary>
        public static string ToPascalCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
            var parts = snakeCase.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0])
                             + parts[i].Substring(1);
            }
            return string.Join("", parts);
        }
    }
}
