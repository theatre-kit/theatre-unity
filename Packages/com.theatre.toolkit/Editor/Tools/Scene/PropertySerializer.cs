using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.Tools.Scene
{
    /// <summary>
    /// Serializes Unity SerializedProperty trees to JSON.
    /// Handles all SerializedPropertyType values and follows Theatre's
    /// wire format conventions (snake_case, vectors as arrays, etc.).
    /// </summary>
    public static class PropertySerializer
    {
        /// <summary>
        /// Detail level for property serialization.
        /// </summary>
        public enum DetailLevel
        {
            /// <summary>Type name and key values only.</summary>
            Summary,

            /// <summary>All visible serialized properties.</summary>
            Full,

            /// <summary>All properties including debug/hidden.</summary>
            Properties
        }

        /// <summary>
        /// Serialize all components on a GameObject to a JArray.
        /// </summary>
        /// <param name="gameObject">Target GameObject.</param>
        /// <param name="detail">Detail level.</param>
        /// <param name="componentFilter">
        /// Optional filter: only serialize components matching these type names.
        /// </param>
        /// <param name="budget">Token budget tracker.</param>
        /// <returns>JArray of component objects.</returns>
        public static JArray SerializeComponents(
            GameObject gameObject,
            DetailLevel detail,
            string[] componentFilter,
            TokenBudget budget)
        {
            var result = new JArray();
            var components = gameObject.GetComponents<Component>();

            foreach (var component in components)
            {
                if (component == null) continue; // Missing script

                var typeName = component.GetType().Name;

                // Apply component filter
                if (componentFilter != null &&
                    !componentFilter.Any(f => string.Equals(f, typeName,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Budget check — estimate before serializing
                if (budget != null && budget.IsExhausted)
                    break;

                var compObj = new JObject();
                compObj["type"] = typeName;

                // Script path for MonoBehaviours
                if (component is MonoBehaviour mb)
                {
                    var script = MonoScript.FromMonoBehaviour(mb);
                    if (script != null)
                    {
                        var path = AssetDatabase.GetAssetPath(script);
                        if (!string.IsNullOrEmpty(path))
                            compObj["script"] = path;
                    }
                }

                if (detail == DetailLevel.Summary)
                {
                    compObj["properties"] = BuildSummaryProperties(component);
                }
                else
                {
                    compObj["properties"] = BuildAllProperties(component,
                        detail == DetailLevel.Properties, budget);
                }

                result.Add(compObj);

                // Track budget
                if (budget != null)
                {
                    var approxJson = compObj.ToString(Formatting.None);
                    budget.Add(approxJson.Length);
                }
            }

            return result;
        }

        /// <summary>
        /// Build summary-level properties for known component types.
        /// For Transform: position, euler_angles, local_scale.
        /// For unknown types: first 3 serialized properties.
        /// </summary>
        private static JObject BuildSummaryProperties(Component component)
        {
            var obj = new JObject();

            if (component is Transform t)
            {
                obj["position"] = ResponseHelpers.ToJArray(t.position);
                obj["euler_angles"] = ResponseHelpers.ToJArray(t.eulerAngles);
                obj["local_scale"] = ResponseHelpers.ToJArray(t.localScale);
            }
            else
            {
                // Generic: first 3 visible properties
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                int count = 0;
                while (prop.NextVisible(enterChildren: false) && count < 3)
                {
                    if (prop.name == "m_Script") continue;
                    var name = GetPropertyName(prop);
                    var value = GetPropertyValue(prop);
                    if (value != null)
                        obj[name] = value;
                    count++;
                }
            }

            return obj;
        }

        /// <summary>
        /// Build all serialized properties for a component.
        /// </summary>
        private static JObject BuildAllProperties(
            Component component,
            bool includeHidden,
            TokenBudget budget)
        {
            var obj = new JObject();
            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            bool enterChildren = true;

            while (includeHidden
                ? prop.Next(enterChildren)
                : prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "m_Script") continue;

                if (budget != null && budget.IsExhausted)
                {
                    obj["_truncated"] = "Budget exhausted — use 'components' filter";
                    break;
                }

                var name = GetPropertyName(prop);
                var value = GetPropertyValue(prop);
                if (value != null)
                    obj[name] = value;
            }

            return obj;
        }

        /// <summary>
        /// Get the snake_case property name, stripping Unity's m_ prefix.
        /// </summary>
        private static string GetPropertyName(SerializedProperty prop)
        {
            var name = ToSnakeCase(prop.name);
            // Strip Unity's internal "m_" prefix
            if (name.StartsWith("m_"))
                name = name.Substring(2);
            return name;
        }

        /// <summary>
        /// Get the value of a SerializedProperty as a JToken.
        /// Returns null for unhandled property types with no children.
        /// </summary>
        private static JToken GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;

                case SerializedPropertyType.Float:
                    return Math.Round(prop.floatValue, 4);

                case SerializedPropertyType.Boolean:
                    return prop.boolValue;

                case SerializedPropertyType.String:
                    return prop.stringValue;

                case SerializedPropertyType.Enum:
                    return ToSnakeCase(
                        prop.enumDisplayNames.Length > prop.enumValueIndex
                            ? prop.enumDisplayNames[prop.enumValueIndex]
                            : prop.enumValueIndex.ToString());

                case SerializedPropertyType.Vector2:
                    return ResponseHelpers.ToJArray(prop.vector2Value);

                case SerializedPropertyType.Vector3:
                    return ResponseHelpers.ToJArray(prop.vector3Value);

                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new JArray(
                        Math.Round(v4.x, 2),
                        Math.Round(v4.y, 2),
                        Math.Round(v4.z, 2),
                        Math.Round(v4.w, 2));

                case SerializedPropertyType.Quaternion:
                    return ResponseHelpers.QuaternionToJArray(prop.quaternionValue);

                case SerializedPropertyType.Color:
                    return ResponseHelpers.ToJArray(prop.colorValue);

                case SerializedPropertyType.Rect:
                    var rect = prop.rectValue;
                    return new JArray(
                        Math.Round(rect.x, 2),
                        Math.Round(rect.y, 2),
                        Math.Round(rect.width, 2),
                        Math.Round(rect.height, 2));

                case SerializedPropertyType.Bounds:
                    var bounds = prop.boundsValue;
                    var boundsObj = new JObject();
                    boundsObj["center"] = ResponseHelpers.ToJArray(bounds.center);
                    boundsObj["size"] = ResponseHelpers.ToJArray(bounds.size);
                    return boundsObj;

                case SerializedPropertyType.ObjectReference:
                    return BuildObjectReference(prop);

                case SerializedPropertyType.ArraySize:
                    // Skip — array contents handled separately
                    return null;

                default:
                    if (prop.isArray)
                    {
                        return BuildArray(prop);
                    }
                    else if (prop.hasChildren)
                    {
                        return BuildNestedObject(prop);
                    }
                    else
                    {
                        return $"<{prop.propertyType}>";
                    }
            }
        }

        /// <summary>
        /// Build an ObjectReference property as a JObject.
        /// </summary>
        private static JToken BuildObjectReference(SerializedProperty prop)
        {
            var obj = prop.objectReferenceValue;
            if (obj == null)
                return JValue.CreateNull();

            var refObj = new JObject();
            refObj["instance_id"] = obj.GetInstanceID();

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
                refObj["asset_path"] = assetPath;
            else
                refObj["name"] = obj.name;

            refObj["type"] = obj.GetType().Name;
            return refObj;
        }

        /// <summary>
        /// Build a serialized array property as a JArray.
        /// </summary>
        private static JArray BuildArray(SerializedProperty prop)
        {
            var arr = new JArray();
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                arr.Add(GetArrayElementValue(element));
            }
            return arr;
        }

        /// <summary>
        /// Build a nested struct/object property as a JObject.
        /// </summary>
        private static JObject BuildNestedObject(SerializedProperty prop)
        {
            var obj = new JObject();
            var child = prop.Copy();
            var endProp = prop.Copy();
            endProp.Next(false); // Move past this property

            child.Next(true); // Enter children
            do
            {
                if (SerializedProperty.EqualContents(child, endProp))
                    break;
                var name = GetPropertyName(child);
                var value = GetPropertyValue(child);
                if (value != null)
                    obj[name] = value;
            } while (child.Next(false));

            return obj;
        }

        /// <summary>
        /// Get a property value for use as an array element (no name).
        /// </summary>
        private static JToken GetArrayElementValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Float:
                    return Math.Round(prop.floatValue, 4);
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                default:
                    return $"<{prop.propertyType}>";
            }
        }

        /// <summary>
        /// Convert a camelCase or PascalCase name to snake_case.
        /// Also handles Unity's "m_FieldName" convention.
        /// </summary>
        public static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && !char.IsUpper(name[i - 1]))
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
