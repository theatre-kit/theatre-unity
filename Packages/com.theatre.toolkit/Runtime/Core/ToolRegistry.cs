using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Theatre.Transport;

namespace Theatre
{
    /// <summary>
    /// Registration for a single MCP tool.
    /// </summary>
    public sealed class ToolRegistration
    {
        /// <summary>MCP tool name (e.g., "theatre_status").</summary>
        public string Name { get; }

        /// <summary>Human-readable description.</summary>
        public string Description { get; }

        /// <summary>JSON Schema for the tool's input parameters.</summary>
        public JToken InputSchema { get; }

        /// <summary>Which group this tool belongs to.</summary>
        public ToolGroup Group { get; }

        /// <summary>
        /// Handler function. Receives parsed arguments, returns JSON string result.
        /// Called on the main thread.
        /// </summary>
        public Func<JToken, string> Handler { get; }

        /// <summary>Optional annotations (readOnlyHint, title).</summary>
        public McpToolAnnotations Annotations { get; }

        /// <summary>
        /// Create a new tool registration.
        /// </summary>
        public ToolRegistration(
            string name,
            string description,
            JToken inputSchema,
            ToolGroup group,
            Func<JToken, string> handler,
            McpToolAnnotations annotations = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description;
            InputSchema = inputSchema;
            Group = group;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Annotations = annotations;
        }
    }

    /// <summary>
    /// Central registry of all MCP tools. Supports group-based filtering
    /// and per-tool disable overrides.
    /// </summary>
    public sealed class ToolRegistry
    {
        private readonly Dictionary<string, ToolRegistration> _tools = new();

        /// <summary>
        /// Register a tool. Replaces any existing tool with the same name.
        /// </summary>
        public void Register(ToolRegistration tool)
        {
            _tools[tool.Name] = tool;
        }

        /// <summary>
        /// Get all tools that are currently visible given the enabled groups
        /// and disabled tool overrides.
        /// </summary>
        public List<McpToolDefinition> ListTools(
            ToolGroup enabledGroups,
            HashSet<string> disabledTools = null)
        {
            var result = new List<McpToolDefinition>();
            foreach (var tool in _tools.Values)
            {
                if ((tool.Group & enabledGroups) == 0)
                    continue;
                if (disabledTools != null && disabledTools.Contains(tool.Name))
                    continue;

                result.Add(new McpToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema,
                    Annotations = tool.Annotations
                });
            }
            return result;
        }

        /// <summary>
        /// Look up a tool by name. Returns null if not found or not enabled.
        /// </summary>
        public ToolRegistration GetTool(
            string name,
            ToolGroup enabledGroups,
            HashSet<string> disabledTools = null)
        {
            if (!_tools.TryGetValue(name, out var tool))
                return null;
            if ((tool.Group & enabledGroups) == 0)
                return null;
            if (disabledTools != null && disabledTools.Contains(name))
                return null;
            return tool;
        }

        /// <summary>Total registered tools (regardless of group filtering).</summary>
        public int Count => _tools.Count;
    }
}
