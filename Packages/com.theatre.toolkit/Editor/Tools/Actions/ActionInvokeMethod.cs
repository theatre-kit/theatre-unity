using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// action:invoke_method — call a public method on a component via reflection.
    /// Limited to methods with 0-3 parameters of simple types.
    /// </summary>
    internal static class ActionInvokeMethod
    {
        private static readonly Type[] AllowedParamTypes = new[]
        {
            typeof(string), typeof(int), typeof(float),
            typeof(bool), typeof(double), typeof(long)
        };

        private const int MaxArgs = 3;

        public static string Execute(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "invoke_method requires Play Mode",
                    "Enter Play Mode first — method invocation modifies runtime state");

            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var componentName = args["component"]?.Value<string>();
            var methodName = args["method"]?.Value<string>();
            var methodArgs = args["arguments"] as JArray;

            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name");

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

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;

            // Find the component
            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (string.Equals(comp.GetType().Name, componentName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"Component '{componentName}' not found on '{path ?? go.name}'",
                    "Use scene_inspect to list all components on this GameObject");

            // Find the method
            var type = component.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            MethodInfo targetMethod = null;
            int argCount = methodArgs?.Count ?? 0;

            foreach (var method in methods)
            {
                if (method.Name != methodName) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != argCount) continue;

                bool allAllowed = true;
                foreach (var p in parameters)
                {
                    if (!IsAllowedType(p.ParameterType))
                    {
                        allAllowed = false;
                        break;
                    }
                }
                if (allAllowed)
                {
                    targetMethod = method;
                    break;
                }
            }

            if (targetMethod == null)
                return ResponseHelpers.ErrorResponse(
                    "property_not_found",
                    $"No public method '{methodName}' with {argCount} simple-type parameters found on '{componentName}'",
                    "invoke_method only supports string, int, float, bool parameters. "
                    + "Use scene_inspect to check available methods.");

            // Convert arguments
            object[] convertedArgs = null;
            if (argCount > 0)
            {
                convertedArgs = new object[argCount];
                var parameters = targetMethod.GetParameters();
                for (int i = 0; i < argCount; i++)
                {
                    try
                    {
                        convertedArgs[i] = methodArgs[i].ToObject(parameters[i].ParameterType);
                    }
                    catch (Exception ex)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            $"Cannot convert argument {i} to {parameters[i].ParameterType.Name}: {ex.Message}",
                            $"Parameter '{parameters[i].Name}' expects {parameters[i].ParameterType.Name}");
                    }
                }
            }

            // Invoke
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

            // Build response
            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["component"] = componentName;
            response["method"] = methodName;

            if (targetMethod.ReturnType != typeof(void) && returnValue != null)
            {
                try
                {
                    response["return_value"] = JToken.FromObject(returnValue);
                }
                catch
                {
                    response["return_value"] = returnValue.ToString();
                }
            }

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool IsAllowedType(Type type)
        {
            foreach (var allowed in AllowedParamTypes)
            {
                if (type == allowed) return true;
            }
            return false;
        }
    }
}
