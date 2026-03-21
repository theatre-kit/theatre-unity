using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Spatial
{
    /// <summary>
    /// MCP tool: spatial_query
    /// Compound tool for spatial questions about the scene.
    /// Operations: nearest, radius, overlap, raycast, linecast,
    ///             path_distance, bounds.
    /// </summary>
    public static class SpatialQueryTool
    {
        private static readonly JToken s_inputSchema;
        private static SpatialIndex s_spatialIndex;

        static SpatialQueryTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""nearest"", ""radius"", ""overlap"",
                                   ""raycast"", ""linecast"",
                                   ""path_distance"", ""bounds""],
                        ""description"": ""The spatial query operation to perform.""
                    },
                    ""origin"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Query center point [x,y,z] or [x,y]. Used by nearest, radius, overlap, raycast.""
                    },
                    ""direction"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Ray direction [x,y,z] or [x,y] (normalized). Used by raycast.""
                    },
                    ""from"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Start point [x,y,z] or [x,y]. Used by linecast, path_distance.""
                    },
                    ""to"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""End point [x,y,z] or [x,y]. Used by linecast, path_distance.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Search radius. Used by radius operation.""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""default"": 5,
                        ""description"": ""Max results for nearest.""
                    },
                    ""max_distance"": {
                        ""type"": ""number"",
                        ""description"": ""Distance cutoff for nearest; max ray length for raycast (default 1000).""
                    },
                    ""all"": {
                        ""type"": ""boolean"",
                        ""default"": false,
                        ""description"": ""Return all hits (raycast) or just first.""
                    },
                    ""shape"": {
                        ""type"": ""string"",
                        ""enum"": [""sphere"", ""circle"", ""box"", ""capsule""],
                        ""description"": ""Overlap shape. Used by overlap.""
                    },
                    ""center"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Shape center for overlap.""
                    },
                    ""size"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Box half-extents [x,y,z], or [radius] for sphere/circle. Used by overlap.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target object path. Used by bounds.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Target object instance_id. Used by bounds.""
                    },
                    ""source"": {
                        ""type"": ""string"",
                        ""enum"": [""renderer"", ""collider"", ""combined""],
                        ""default"": ""combined"",
                        ""description"": ""Bounds source. Used by bounds.""
                    },
                    ""layer_mask"": {
                        ""type"": ""integer"",
                        ""description"": ""Physics layer mask for overlap/raycast/linecast.""
                    },
                    ""physics"": {
                        ""type"": ""string"",
                        ""enum"": [""3d"", ""2d""],
                        ""description"": ""Physics mode override. Default: auto-detected from scene.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter results to objects with these components. Used by nearest, radius.""
                    },
                    ""exclude_tags"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Exclude objects with these tags. Used by nearest, radius.""
                    },
                    ""sort_by"": {
                        ""type"": ""string"",
                        ""enum"": [""distance"", ""name""],
                        ""default"": ""distance"",
                        ""description"": ""Sort order for radius results.""
                    },
                    ""agent_type_id"": {
                        ""type"": ""integer"",
                        ""description"": ""NavMesh agent type for path_distance.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Token budget for nearest/radius results.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "spatial_query",
                description: "Spatial questions about the scene: find nearest "
                    + "objects, query within radius, physics overlap/raycast/"
                    + "linecast, NavMesh path distance, bounding boxes. "
                    + "Use 'nearest' or 'radius' for transform-based queries "
                    + "(works in Edit Mode). Use 'overlap', 'raycast', "
                    + "'linecast' for physics queries (requires Play Mode).",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageQuery,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        /// <summary>
        /// Get or create the shared spatial index.
        /// </summary>
        internal static SpatialIndex GetIndex()
        {
            if (s_spatialIndex == null)
                s_spatialIndex = new SpatialIndex();
            return s_spatialIndex;
        }

        /// <summary>
        /// Invalidate the spatial index (called on scene change).
        /// </summary>
        internal static void InvalidateIndex()
        {
            s_spatialIndex?.Invalidate();
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"nearest\", \"origin\": [0,0,0]}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: nearest, radius, overlap, raycast, "
                    + "linecast, path_distance, bounds");
            }

            // Play mode gate for physics operations
            var playModeError = PhysicsMode.CheckPlayModeRequired(operation);
            if (playModeError != null)
                return playModeError;

            try
            {
                return operation switch
                {
                    "nearest" => SpatialQueryNearest.Execute(args),
                    "radius" => SpatialQueryRadius.Execute(args),
                    "overlap" => SpatialQueryOverlap.Execute(args),
                    "raycast" => SpatialQueryRaycast.Execute(args),
                    "linecast" => SpatialQueryLinecast.Execute(args),
                    "path_distance" => SpatialQueryPathDistance.Execute(args),
                    "bounds" => SpatialQueryBounds.Execute(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: nearest, radius, overlap, raycast, "
                        + "linecast, path_distance, bounds")
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[Theatre] spatial_query:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"spatial_query:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
