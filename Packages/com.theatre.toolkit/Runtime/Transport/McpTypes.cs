using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Transport
{
    // --- Initialize ---

    /// <summary>
    /// Parameters for the MCP initialize request.
    /// </summary>
    public sealed class McpInitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public JToken Capabilities { get; set; }

        [JsonProperty("clientInfo")]
        public McpImplementationInfo ClientInfo { get; set; }
    }

    /// <summary>
    /// Result for the MCP initialize response.
    /// </summary>
    public sealed class McpInitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public McpServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public McpImplementationInfo ServerInfo { get; set; }

        [JsonProperty("instructions", NullValueHandling = NullValueHandling.Ignore)]
        public string Instructions { get; set; }
    }

    /// <summary>
    /// Name and version of an MCP implementation (client or server).
    /// </summary>
    public sealed class McpImplementationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// Server capability declarations sent during initialization.
    /// </summary>
    public sealed class McpServerCapabilities
    {
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public McpToolCapability Tools { get; set; }

        [JsonProperty("logging", NullValueHandling = NullValueHandling.Ignore)]
        public object Logging { get; set; }
    }

    /// <summary>
    /// Declares tool-related capabilities.
    /// </summary>
    public sealed class McpToolCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    // --- Tools ---

    /// <summary>
    /// MCP tool definition as returned in tools/list.
    /// </summary>
    public sealed class McpToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JToken InputSchema { get; set; }

        [JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
        public McpToolAnnotations Annotations { get; set; }
    }

    /// <summary>
    /// Optional tool annotations providing hints to MCP clients.
    /// </summary>
    public sealed class McpToolAnnotations
    {
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("readOnlyHint", NullValueHandling = NullValueHandling.Ignore)]
        public bool? ReadOnlyHint { get; set; }
    }

    /// <summary>
    /// Result envelope for tools/list.
    /// </summary>
    public sealed class McpToolsListResult
    {
        [JsonProperty("tools")]
        public List<McpToolDefinition> Tools { get; set; } = new();
    }

    /// <summary>
    /// Parameters for tools/call.
    /// </summary>
    public sealed class McpToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
        public JToken Arguments { get; set; }
    }

    /// <summary>
    /// Result envelope for tools/call.
    /// </summary>
    public sealed class McpToolCallResult
    {
        [JsonProperty("content")]
        public List<McpContentItem> Content { get; set; } = new();

        [JsonProperty("isError", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsError { get; set; }
    }

    /// <summary>
    /// A single content item in a tools/call result.
    /// </summary>
    public sealed class McpContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }
}
