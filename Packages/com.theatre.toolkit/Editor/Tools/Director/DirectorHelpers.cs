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

                if (!PropertyValueHelpers.SetPropertyValue(sp, value, out var setError))
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
        /// Delegates to <see cref="PropertyValueHelpers.ReadPropertyValue"/>.
        /// </summary>
        internal static JToken ReadPropertyValue(SerializedProperty prop)
            => PropertyValueHelpers.ReadPropertyValue(prop);

        /// <summary>
        /// Set a single SerializedProperty value from a JToken.
        /// Delegates to <see cref="PropertyValueHelpers.SetPropertyValue"/>.
        /// </summary>
        internal static bool SetPropertyValue(
            SerializedProperty prop, JToken value, out string error)
            => PropertyValueHelpers.SetPropertyValue(prop, value, out error);

        /// <summary>
        /// Resolve a Unity Object from a JToken value for ObjectReference assignment.
        /// Delegates to <see cref="PropertyValueHelpers.ResolveObjectReference"/>.
        /// </summary>
        internal static UnityEngine.Object ResolveObjectReference(
            JToken value, SerializedProperty prop, out string error)
            => PropertyValueHelpers.ResolveObjectReference(value, prop, out error);

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
