using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Director.Shared
{
    /// <summary>
    /// Property value conversion utilities for Director tool handlers.
    /// Handles reading and writing SerializedProperty values from JToken,
    /// including ObjectReference resolution and sub-asset support.
    /// </summary>
    internal static class PropertyValueHelpers
    {
#if UNITY_EDITOR
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
            var names = new List<string>();
            foreach (var asset in allAssets)
            {
                if (asset != null && !string.IsNullOrEmpty(asset.name))
                    names.Add(asset.name);
            }

            error = $"Sub-asset '{subAssetName}' not found in '{mainPath}'. "
                  + $"Available: {string.Join(", ", names)}";
            return null;
        }

        /// <summary>
        /// Get the expected System.Type for an ObjectReference SerializedProperty.
        /// Uses reflection on the target object's field declaration.
        /// Returns null if the type cannot be determined (fallback: no type check).
        /// </summary>
        internal static Type GetFieldType(SerializedProperty prop)
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
#endif
    }
}
