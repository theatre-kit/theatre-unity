using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: material_op
    /// Compound tool for creating and modifying Material assets in the Unity Editor.
    /// Operations: create, set_properties, set_shader, list_properties.
    /// </summary>
    public static class MaterialOpTool
    {
        private static readonly JToken s_inputSchema;

        static MaterialOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""set_properties"", ""set_shader"", ""list_properties""],
                        ""description"": ""The material operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the material (e.g. 'Assets/Materials/MyMat.mat').""
                    },
                    ""shader"": {
                        ""type"": ""string"",
                        ""description"": ""Shader name (e.g. 'Standard', 'Universal Render Pipeline/Lit').""
                    },
                    ""properties"": {
                        ""type"": ""object"",
                        ""description"": ""Shader property name to value pairs. Numbers → SetFloat, [r,g,b,a] arrays → SetColor, texture paths → SetTexture.""
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
                name: "material_op",
                description: "Create and modify Material assets in the Unity Editor. "
                    + "Operations: create, set_properties, set_shader, list_properties. "
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
                "material_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"          => Create(args),
                    "set_properties"  => SetProperties(args),
                    "set_shader"      => SetShader(args),
                    "list_properties" => ListProperties(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, set_properties, set_shader, list_properties")
                },
                "create, set_properties, set_shader, list_properties");

        /// <summary>Create a new Material asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".mat");
            if (pathError != null) return pathError;

            var shaderName = args["shader"]?.Value<string>();
            if (string.IsNullOrEmpty(shaderName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'shader' parameter",
                    "Provide a shader name such as 'Standard' or 'Universal Render Pipeline/Lit'");

            // Dry run validation
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                var dryErrors = new List<string>();
                var s = Shader.Find(shaderName);
                if (s == null)
                    dryErrors.Add($"Shader '{shaderName}' not found");
                return (dryErrors.Count == 0, dryErrors);
            });
            if (dryRun != null) return dryRun;

            var shader = Shader.Find(shaderName);
            if (shader == null)
                return ResponseHelpers.ErrorResponse(
                    "shader_not_found",
                    $"Shader '{shaderName}' not found",
                    "Check the shader name. 'Standard' always exists. URP shaders require URP installed.");

            var material = new Material(shader);

            // Apply initial properties if provided
            var properties = args["properties"] as JObject;
            if (properties != null)
                ApplyMaterialProperties(material, properties);

            // Ensure parent directory exists
            EnsureParentDirectory(assetPath);

            AssetDatabase.CreateAsset(material, assetPath);
            Undo.RegisterCreatedObjectUndo(material, "Theatre material_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["shader"] = shader.name;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set shader properties on an existing Material.</summary>
        internal static string SetProperties(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var properties = args["properties"] as JObject;
            if (properties == null || !properties.HasValues)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'properties' parameter",
                    "Provide a 'properties' object with shader property name/value pairs");

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Material not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .mat");

            Undo.RecordObject(material, "Theatre material_op:set_properties");
            int propertiesSet = ApplyMaterialProperties(material, properties);
            EditorUtility.SetDirty(material);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_properties";
            response["asset_path"] = assetPath;
            response["properties_set"] = propertiesSet;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Change the shader on an existing Material.</summary>
        internal static string SetShader(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var shaderName = args["shader"]?.Value<string>();
            if (string.IsNullOrEmpty(shaderName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'shader' parameter",
                    "Provide a shader name such as 'Standard'");

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Material not found at '{assetPath}'",
                    "Check the asset path is correct");

            var newShader = Shader.Find(shaderName);
            if (newShader == null)
                return ResponseHelpers.ErrorResponse(
                    "shader_not_found",
                    $"Shader '{shaderName}' not found",
                    "Check the shader name. 'Standard' always exists.");

            var oldShaderName = material.shader != null ? material.shader.name : "none";

            Undo.RecordObject(material, "Theatre material_op:set_shader");
            material.shader = newShader;
            EditorUtility.SetDirty(material);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_shader";
            response["asset_path"] = assetPath;
            response["old_shader"] = oldShaderName;
            response["shader"] = newShader.name;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all shader properties on a Material.</summary>
        internal static string ListProperties(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Material not found at '{assetPath}'",
                    "Check the asset path is correct");

            var shader = material.shader;
            var propsArray = new JArray();

            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    var propName = shader.GetPropertyName(i);
                    var propType = shader.GetPropertyType(i);

                    var propObj = new JObject();
                    propObj["name"] = propName;

                    switch (propType)
                    {
                        case ShaderPropertyType.Color:
                            propObj["type"] = "color";
                            var col = material.GetColor(propName);
                            propObj["value"] = new JArray(
                                Math.Round(col.r, 3),
                                Math.Round(col.g, 3),
                                Math.Round(col.b, 3),
                                Math.Round(col.a, 3));
                            break;

                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            propObj["type"] = propType == ShaderPropertyType.Range ? "range" : "float";
                            propObj["value"] = Math.Round(material.GetFloat(propName), 4);
                            break;

                        case ShaderPropertyType.Vector:
                            propObj["type"] = "vector";
                            var vec = material.GetVector(propName);
                            propObj["value"] = new JArray(
                                Math.Round(vec.x, 3),
                                Math.Round(vec.y, 3),
                                Math.Round(vec.z, 3),
                                Math.Round(vec.w, 3));
                            break;

                        case ShaderPropertyType.Texture:
                            propObj["type"] = "texture";
                            var tex = material.GetTexture(propName);
                            propObj["value"] = tex != null
                                ? (JToken)AssetDatabase.GetAssetPath(tex)
                                : JValue.CreateNull();
                            break;

                        case ShaderPropertyType.Int:
                            propObj["type"] = "int";
                            propObj["value"] = material.GetInteger(propName);
                            break;

                        default:
                            propObj["type"] = propType.ToString().ToLowerInvariant();
                            propObj["value"] = JValue.CreateNull();
                            break;
                    }

                    propsArray.Add(propObj);
                }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_properties";
            response["asset_path"] = assetPath;
            response["shader"] = shader != null ? shader.name : null;
            response["properties"] = propsArray;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static int ApplyMaterialProperties(Material material, JObject properties)
        {
            int count = 0;
            foreach (var prop in properties.Properties())
            {
                var name = prop.Name;
                var value = prop.Value;

                try
                {
                    if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                    {
                        material.SetFloat(name, value.Value<float>());
                        count++;
                    }
                    else if (value.Type == JTokenType.Boolean)
                    {
                        material.SetFloat(name, value.Value<bool>() ? 1f : 0f);
                        count++;
                    }
                    else if (value is JArray arr)
                    {
                        if (arr.Count >= 4)
                        {
                            // 4-element arrays are treated as Color (r,g,b,a)
                            material.SetColor(name, new Color(
                                arr[0].Value<float>(),
                                arr[1].Value<float>(),
                                arr[2].Value<float>(),
                                arr[3].Value<float>()));
                            count++;
                        }
                        else if (arr.Count >= 2)
                        {
                            float x = arr[0].Value<float>();
                            float y = arr[1].Value<float>();
                            float z = arr.Count >= 3 ? arr[2].Value<float>() : 0f;
                            material.SetVector(name, new Vector4(x, y, z, 0f));
                            count++;
                        }
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        var str = value.Value<string>();
                        if (str != null && (str.StartsWith("Assets/") || str.StartsWith("Packages/")))
                        {
                            var tex = AssetDatabase.LoadAssetAtPath<Texture>(str);
                            material.SetTexture(name, tex);
                            count++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] material_op: Could not set property '{name}': {ex.Message}");
                }
            }
            return count;
        }

        private static void EnsureParentDirectory(string assetPath)
        {
            // assetPath is relative (Assets/...), build parent folder path
            var lastSlash = assetPath.LastIndexOf('/');
            if (lastSlash <= 0) return;

            var parentPath = assetPath.Substring(0, lastSlash);
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                // Split into grandparent + folderName and recursively create
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
    }
}
