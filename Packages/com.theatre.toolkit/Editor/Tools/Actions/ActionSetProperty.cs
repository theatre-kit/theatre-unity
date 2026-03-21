using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_property — set a serialized property on a component.
    /// </summary>
    internal static class ActionSetProperty
    {
        public static string Execute(JObject args)
        {
            var componentName = args["component"]?.Value<string>();
            var propertyName = args["property"]?.Value<string>();
            var value = args["value"];

            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name, e.g., 'Health', 'Transform'");

            if (string.IsNullOrEmpty(propertyName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property' parameter",
                    "Provide the property name, e.g., 'current_hp', 'position'");

            if (value == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'value' parameter",
                    "Provide the value to set");

            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            // Find the component
            var component = ObjectResolver.FindComponent(go, componentName);

            if (component == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"Component '{componentName}' not found on '{go.name}'",
                    "Use scene_inspect to list all components on this GameObject");

#if UNITY_EDITOR
            var so = new SerializedObject(component);

            // Try direct name, then with m_ prefix
            var prop = so.FindProperty(propertyName);
            if (prop == null)
                prop = so.FindProperty("m_" + ToPascalCase(propertyName));
            if (prop == null)
                prop = so.FindProperty(ToPascalCase(propertyName));
            if (prop == null)
                prop = so.FindProperty("m_" + propertyName);

            if (prop == null)
                return ResponseHelpers.ErrorResponse(
                    "property_not_found",
                    $"Property '{propertyName}' not found on component '{componentName}'",
                    "Use scene_inspect with component filter to see available properties");

            // Read previous value
            var previousValue = ReadCurrentValue(prop);

            // Set the value
            if (!SetPropertyValue(prop, value, out var setError))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Cannot set '{propertyName}': {setError}",
                    "Check the property type and provide a compatible value");

            so.ApplyModifiedProperties();
#endif

            var response = new JObject();
            response["result"] = "ok";
            ResponseHelpers.AddIdentity(response, go);
            response["component"] = componentName;
            response["property"] = propertyName;
            response["value"] = value;
#if UNITY_EDITOR
            response["previous_value"] = previousValue;
#endif
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

#if UNITY_EDITOR
        private static JToken ReadCurrentValue(SerializedProperty prop)
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
                default: return prop.propertyType.ToString();
            }
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
#endif

        private static string ToPascalCase(string snakeCase)
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
