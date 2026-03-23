#if THEATRE_HAS_PROBUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEditor;
using UnityEditor.ProBuilder;
using UEditorUtility = UnityEditor.EditorUtility;
using Theatre.Editor.Tools.Director.Shared;

namespace Theatre.Editor.Tools.Director.Spatial
{
    /// <summary>
    /// MCP tool: probuilder_op
    /// Compound tool for ProBuilder mesh creation and editing.
    /// Operations: create_shape, extrude_faces, set_material, merge, boolean_op, export_mesh.
    /// Requires com.unity.probuilder.
    /// </summary>
    public static class ProBuilderOpTool
    {
        private static readonly JToken s_inputSchema;

        static ProBuilderOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_shape"", ""extrude_faces"", ""set_material"", ""merge"", ""boolean_op"", ""export_mesh""],
                        ""description"": ""The ProBuilder operation to perform.""
                    },
                    ""shape"": {
                        ""type"": ""string"",
                        ""description"": ""Shape type for create_shape: cube, cylinder, sphere, plane, stair, arch, door, pipe, cone, torus, prism.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path to a ProBuilder mesh GameObject.""
                    },
                    ""paths"": {
                        ""type"": ""array"",
                        ""description"": ""Array of hierarchy paths to ProBuilder mesh GameObjects (for merge).""
                    },
                    ""path_a"": {
                        ""type"": ""string"",
                        ""description"": ""First mesh path for boolean_op.""
                    },
                    ""path_b"": {
                        ""type"": ""string"",
                        ""description"": ""Second mesh path for boolean_op.""
                    },
                    ""faces"": {
                        ""type"": ""array"",
                        ""description"": ""Array of face indices for extrude_faces or set_material.""
                    },
                    ""distance"": {
                        ""type"": ""number"",
                        ""description"": ""Extrusion distance for extrude_faces. Default 1.""
                    },
                    ""material"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to a Material for set_material.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Output asset path for export_mesh (must end in .asset).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""World position [x, y, z] for create_shape.""
                    },
                    ""size"": {
                        ""type"": ""array"",
                        ""description"": ""Size [x, y, z] or single float for create_shape.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""GameObject name for create_shape.""
                    },
                    ""boolean_operation"": {
                        ""type"": ""string"",
                        ""description"": ""Boolean operation type: union, subtract, intersect.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "probuilder_op",
                description: "ProBuilder mesh creation and editing. "
                    + "Operations: create_shape, extrude_faces, set_material, merge, boolean_op, export_mesh. "
                    + "Requires com.unity.probuilder. All mutations are undoable.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorSpatial,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "probuilder_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create_shape"  => CreateShape(args),
                    "extrude_faces" => ExtrudeFaces(args),
                    "set_material"  => SetMaterial(args),
                    "merge"         => Merge(args),
                    "boolean_op"    => BooleanOp(args),
                    "export_mesh"   => ExportMesh(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_shape, extrude_faces, set_material, merge, boolean_op, export_mesh")
                },
                "create_shape, extrude_faces, set_material, merge, boolean_op, export_mesh");

        // --- Sub-handlers ---

        /// <summary>Create a ProBuilder shape.</summary>
        internal static string CreateShape(JObject args)
        {
            var shapeName = args["shape"]?.Value<string>();
            if (string.IsNullOrEmpty(shapeName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'shape' parameter",
                    "Valid shapes: cube, cylinder, sphere, plane, stair, arch, door, pipe, cone, torus, prism");

            if (!TryParseShapeType(shapeName, out var shapeType))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown shape '{shapeName}'",
                    "Valid shapes: cube, cylinder, sphere, plane, stair, arch, door, pipe, cone, torus, prism");

            var mesh = ShapeGenerator.CreateShape(shapeType);
            if (mesh == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"ShapeGenerator.CreateShape returned null for shape '{shapeName}'",
                    "Check the Unity Console for details");

            // Apply optional position
            var posArr = args["position"] as JArray;
            if (posArr != null && posArr.Count >= 3)
            {
                mesh.gameObject.transform.position = new Vector3(
                    posArr[0].Value<float>(),
                    posArr[1].Value<float>(),
                    posArr[2].Value<float>());
            }

            // Apply optional name
            var name = args["name"]?.Value<string>();
            if (!string.IsNullOrEmpty(name))
                mesh.gameObject.name = name;

            Undo.RegisterCreatedObjectUndo(mesh.gameObject, "Theatre probuilder_op:create_shape");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_shape";
            response["shape"] = shapeName;
            ResponseHelpers.AddIdentity(response, mesh.gameObject);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Extrude selected faces by a distance.</summary>
        internal static string ExtrudeFaces(JObject args)
        {
            var mesh = ResolveProBuilderMesh(args["path"]?.Value<string>(), out var error);
            if (mesh == null) return error;

            var facesToken = args["faces"] as JArray;
            if (facesToken == null || facesToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'faces' parameter",
                    "Provide an array of face indices to extrude, e.g. [0, 1, 2]");

            float distance = args["distance"]?.Value<float>() ?? 1f;

            var allFaces = mesh.faces;
            var selectedFaces = new List<Face>();
            foreach (var token in facesToken)
            {
                int idx = token.Value<int>();
                if (idx >= 0 && idx < allFaces.Count)
                    selectedFaces.Add(allFaces[idx]);
            }

            if (selectedFaces.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "No valid face indices found",
                    $"Mesh has {allFaces.Count} faces (indices 0-{allFaces.Count - 1})");

            Undo.RecordObject(mesh, "Theatre probuilder_op:extrude_faces");
            mesh.Extrude(selectedFaces, ExtrudeMethod.FaceNormal, distance);
            mesh.ToMesh();
            mesh.Refresh();
            UEditorUtility.SetDirty(mesh);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "extrude_faces";
            response["extruded"] = selectedFaces.Count;
            response["distance"] = distance;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Assign a material to selected faces.</summary>
        internal static string SetMaterial(JObject args)
        {
            var mesh = ResolveProBuilderMesh(args["path"]?.Value<string>(), out var error);
            if (mesh == null) return error;

            var materialPath = args["material"]?.Value<string>();
            if (string.IsNullOrEmpty(materialPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'material' parameter",
                    "Provide the asset path to a Material, e.g. 'Assets/Materials/Stone.mat'");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Material not found at '{materialPath}'",
                    "Check the asset path is correct and the asset is a Material");

            var facesToken = args["faces"] as JArray;
            if (facesToken == null || facesToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'faces' parameter",
                    "Provide an array of face indices to assign material to");

            var allFaces = mesh.faces;
            var selectedFaces = new List<Face>();
            foreach (var token in facesToken)
            {
                int idx = token.Value<int>();
                if (idx >= 0 && idx < allFaces.Count)
                    selectedFaces.Add(allFaces[idx]);
            }

            if (selectedFaces.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "No valid face indices found",
                    $"Mesh has {allFaces.Count} faces (indices 0-{allFaces.Count - 1})");

            Undo.RecordObject(mesh, "Theatre probuilder_op:set_material");
            mesh.SetMaterial(selectedFaces, mat);
            mesh.ToMesh();
            mesh.Refresh();
            UEditorUtility.SetDirty(mesh);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_material";
            response["material"] = materialPath;
            response["faces"] = selectedFaces.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Merge multiple ProBuilder meshes into the first.</summary>
        internal static string Merge(JObject args)
        {
            var pathsToken = args["paths"] as JArray;
            if (pathsToken == null || pathsToken.Count < 2)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or insufficient 'paths' parameter",
                    "Provide at least 2 hierarchy paths to ProBuilder meshes to merge");

            var meshes = new List<ProBuilderMesh>();
            foreach (var token in pathsToken)
            {
                var p = token.Value<string>();
                var m = ResolveProBuilderMesh(p, out var err);
                if (m == null) return err;
                meshes.Add(m);
            }

            Undo.RecordObjects(meshes.Cast<UnityEngine.Object>().ToArray(), "Theatre probuilder_op:merge");
            CombineMeshes.Combine(meshes, meshes[0]);
            meshes[0].ToMesh();
            meshes[0].Refresh();
            UEditorUtility.SetDirty(meshes[0]);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "merge";
            response["merged"] = meshes.Count;
            ResponseHelpers.AddIdentity(response, meshes[0].gameObject);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Boolean CSG operation between two ProBuilder meshes.</summary>
        internal static string BooleanOp(JObject args)
        {
            // ProBuilder CSG is available via UnityEngine.ProBuilder.Csg namespace
            // In ProBuilder 6.x the CSG API may not be publicly exposed
            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "Boolean CSG operations are not available in this version of ProBuilder",
                "ProBuilder 6.x does not expose a public CSG API. Use manual mesh editing as an alternative.");
        }

        /// <summary>Export a ProBuilder mesh to a .asset file.</summary>
        internal static string ExportMesh(JObject args)
        {
            var path = args["path"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide the hierarchy path to a ProBuilder mesh GameObject");

            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            var mesh = ResolveProBuilderMesh(path, out var error);
            if (mesh == null) return error;

            var meshFilter = mesh.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"No MeshFilter with a mesh found on '{path}'",
                    "Ensure the ProBuilder mesh has been built (ToMesh/Refresh called)");

            var meshCopy = UnityEngine.Object.Instantiate(meshFilter.sharedMesh);
            meshCopy.name = meshFilter.sharedMesh.name + "_exported";

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(meshCopy, assetPath);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "export_mesh";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        /// <summary>
        /// Resolve a ProBuilderMesh component from a hierarchy path.
        /// Returns null and sets error on failure.
        /// </summary>
        private static ProBuilderMesh ResolveProBuilderMesh(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide the hierarchy path to a ProBuilder mesh GameObject");
                return null;
            }

            var resolved = ObjectResolver.Resolve(path: path);
            if (!resolved.Success)
            {
                error = ResponseHelpers.ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);
                return null;
            }

            var mesh = resolved.GameObject.GetComponent<ProBuilderMesh>();
            if (mesh == null)
            {
                error = ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"No ProBuilderMesh component found on '{path}'",
                    "Ensure the target GameObject was created with ProBuilder (create_shape or similar)");
                return null;
            }

            return mesh;
        }

        /// <summary>Map a shape name string to a ShapeType enum value.</summary>
        private static bool TryParseShapeType(string name, out ShapeType shapeType)
        {
            shapeType = ShapeType.Cube;
            switch (name.ToLowerInvariant())
            {
                case "cube":     shapeType = ShapeType.Cube;     return true;
                case "cylinder": shapeType = ShapeType.Cylinder; return true;
                case "sphere":   shapeType = ShapeType.Sphere;   return true;
                case "plane":    shapeType = ShapeType.Plane;    return true;
                case "stair":    shapeType = ShapeType.Stair;    return true;
                case "arch":     shapeType = ShapeType.Arch;     return true;
                case "door":     shapeType = ShapeType.Door;     return true;
                case "pipe":     shapeType = ShapeType.Pipe;     return true;
                case "cone":     shapeType = ShapeType.Cone;     return true;
                case "torus":    shapeType = ShapeType.Torus;    return true;
                case "prism":    shapeType = ShapeType.Prism;    return true;
                default:         return false;
            }
        }

    }
}
#endif
