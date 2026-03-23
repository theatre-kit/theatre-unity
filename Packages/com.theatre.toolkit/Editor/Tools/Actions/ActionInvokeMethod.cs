using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools.Actions
{
    /// <summary>
    /// action:invoke_method — call a public method on a component via reflection.
    /// Supports instance methods (Play Mode required) and static methods (Edit Mode).
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

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
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
