using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools
{
    /// <summary>
    /// Shared dispatch boilerplate for compound MCP tools.
    /// Each compound tool's Execute method validates arguments,
    /// extracts the operation string, and then delegates to a switch.
    /// This class encapsulates that repetitive outer shell.
    /// </summary>
    public static class CompoundToolDispatcher
    {
        /// <summary>
        /// Execute a compound tool operation.
        /// Validates that arguments is a non-null JObject, extracts the "operation" field,
        /// logs and wraps any exception from the handler, and returns a JSON string.
        /// </summary>
        /// <param name="toolName">The MCP tool name (e.g. "material_op"), used in error messages.</param>
        /// <param name="arguments">The raw JToken arguments from the MCP request.</param>
        /// <param name="handler">
        /// A function that receives the validated (JObject args, string operation) pair
        /// and returns the JSON result string. Should use a switch expression on operation
        /// and return <see cref="ResponseHelpers.ErrorResponse"/> for unknown operations.
        /// </param>
        /// <param name="validOperations">
        /// A human-readable list of valid operations for error messages
        /// (e.g. "create, delete, list"). Used only in validation error messages.
        /// </param>
        /// <returns>A JSON string, always a valid MCP response.</returns>
        public static string Execute(
            string toolName,
            JToken arguments,
            Func<JObject, string, string> handler,
            string validOperations)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    $"Provide {{\"operation\": \"...\"}}. Valid operations: {validOperations}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    $"Valid operations: {validOperations}");
            }

            try
            {
                return handler(args, operation);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] {toolName}:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"{toolName}:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
