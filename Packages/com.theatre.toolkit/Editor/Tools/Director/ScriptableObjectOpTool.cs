using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using Theatre.Editor.Tools.Scene;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: scriptable_object_op
    /// Compound tool for creating and modifying ScriptableObject assets in the Unity Editor.
    /// Operations: create, set_fields, list_fields, find_by_type.
    /// </summary>
    public static class ScriptableObjectOpTool
    {
        private static readonly JToken s_inputSchema;

        static ScriptableObjectOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""set_fields"", ""list_fields"", ""find_by_type""],
                        ""description"": ""The ScriptableObject operation to perform.""
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""description"": ""ScriptableObject type name (e.g. 'MyConfig'). Used by create and find_by_type.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the ScriptableObject (e.g. 'Assets/Data/MyConfig.asset').""
                    },
                    ""fields"": {
                        ""type"": ""object"",
                        ""description"": ""Field name to value pairs to set on the ScriptableObject.""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate only — do not mutate. Returns would_succeed and errors.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scriptable_object_op",
                description: "Create and modify ScriptableObject assets in the Unity Editor. "
                    + "Operations: create, set_fields, list_fields, find_by_type. "
                    + "Supports dry_run to validate without mutating. All mutations are undoable.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAsset,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "scriptable_object_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"       => Create(args),
                    "set_fields"   => SetFields(args),
                    "list_fields"  => ListFields(args),
                    "find_by_type" => FindByType(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, set_fields, list_fields, find_by_type")
                },
                "create, set_fields, list_fields, find_by_type");

        /// <summary>Create a new ScriptableObject asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var typeName = args["type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'type' parameter",
                    "Provide a ScriptableObject type name such as 'MyConfig'");

            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            // Resolve the type
            var soType = DirectorHelpers.ResolveScriptableObjectType(typeName, out var typeError);
            if (soType == null) return typeError;

            // Dry run validation
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                return (true, new List<string>());
            });
            if (dryRun != null) return dryRun;

            var instance = ScriptableObject.CreateInstance(soType);

            // Apply initial fields if provided
            var fields = args["fields"] as JObject;
            if (fields != null && fields.HasValues)
                DirectorHelpers.SetFields(instance, fields);

            // Ensure parent directory exists
            DirectorHelpers.EnsureParentDirectory(assetPath);

            AssetDatabase.CreateAsset(instance, assetPath);
            Undo.RegisterCreatedObjectUndo(instance, "Theatre scriptable_object_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["type"] = soType.Name;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set fields on an existing ScriptableObject asset.</summary>
        internal static string SetFields(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<ScriptableObject>(
                args, out var so, out var assetPath);
            if (loadError != null) return loadError;

            var fields = args["fields"] as JObject;
            if (fields == null || !fields.HasValues)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'fields' parameter",
                    "Provide a 'fields' object with field name/value pairs");

            Undo.RecordObject(so, "Theatre scriptable_object_op:set_fields");
            var (fieldsSet, errors) = DirectorHelpers.SetFields(so, fields);
            EditorUtility.SetDirty(so);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_fields";
            response["asset_path"] = assetPath;
            response["fields_set"] = fieldsSet;
            if (errors.Count > 0)
            {
                var errArr = new JArray();
                foreach (var e in errors) errArr.Add(e);
                response["errors"] = errArr;
            }
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all visible serialized fields on a ScriptableObject asset.</summary>
        internal static string ListFields(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<ScriptableObject>(
                args, out var so, out var assetPath);
            if (loadError != null) return loadError;

            var soSerialized = new SerializedObject(so);
            var fieldsArray = new JArray();
            var iterator = soSerialized.GetIterator();

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (iterator.name == "m_Script") continue;

                    var fieldName = PropertySerializer.ToSnakeCase(iterator.name);
                    // Strip Unity m_ prefix
                    if (fieldName.StartsWith("m_"))
                        fieldName = fieldName.Substring(2);

                    var fieldObj = new JObject();
                    fieldObj["name"] = fieldName;
                    fieldObj["type"] = iterator.propertyType.ToString().ToLowerInvariant();

                    var value = GetSerializedPropertyValue(iterator);
                    fieldObj["value"] = value ?? JValue.CreateNull();

                    fieldsArray.Add(fieldObj);
                }
                while (iterator.NextVisible(false));
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_fields";
            response["asset_path"] = assetPath;
            response["type"] = so.GetType().Name;
            response["fields"] = fieldsArray;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Find all ScriptableObject assets of a given type.</summary>
        internal static string FindByType(JObject args)
        {
            var typeName = args["type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'type' parameter",
                    "Provide a ScriptableObject type name such as 'MyConfig' or 'ScriptableObject'");

            var guids = AssetDatabase.FindAssets($"t:{typeName}");
            var assetsArray = new JArray();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var assetObj = new JObject();
                assetObj["asset_path"] = path;
                assetObj["name"] = name;
                assetsArray.Add(assetObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "find_by_type";
            response["type"] = typeName;
            response["assets"] = assetsArray;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static JToken GetSerializedPropertyValue(SerializedProperty prop)
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
                    return prop.enumDisplayNames.Length > prop.enumValueIndex
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    return ResponseHelpers.ToJArray(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return ResponseHelpers.ToJArray(prop.vector3Value);
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new JArray(
                        Math.Round(v4.x, 3),
                        Math.Round(v4.y, 3),
                        Math.Round(v4.z, 3),
                        Math.Round(v4.w, 3));
                case SerializedPropertyType.Color:
                    return ResponseHelpers.ToJArray(prop.colorValue);
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                    {
                        var refPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                        return string.IsNullOrEmpty(refPath)
                            ? (JToken)prop.objectReferenceValue.name
                            : refPath;
                    }
                    return JValue.CreateNull();
                default:
                    return null;
            }
        }

    }
}
