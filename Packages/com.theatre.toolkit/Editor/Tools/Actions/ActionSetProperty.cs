using Newtonsoft.Json.Linq;
using Theatre;
using Theatre.Editor.Tools.Director;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Actions
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
            var prop = DirectorHelpers.FindPropertyFuzzy(so, propertyName);

            if (prop == null)
            {
                var available = DirectorHelpers.ListPropertyNames(so, propertyName);
                var availStr = available.Count > 0
                    ? $" Available: {string.Join(", ", available)}"
                    : "";
                return ResponseHelpers.ErrorResponse(
                    "property_not_found",
                    $"Property '{propertyName}' not found on component '{componentName}'.{availStr}",
                    "Use scene_inspect with component filter to see all properties. "
                    + "Property names are Unity serialized field names in snake_case, "
                    + "not C# API property names (e.g. 'materials' not 'shared_material').");
            }

            // Read previous value
            var previousValue = DirectorHelpers.ReadPropertyValue(prop);

            // Set the value
            if (!DirectorHelpers.SetPropertyValue(prop, value, out var setError))
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

    }
}
