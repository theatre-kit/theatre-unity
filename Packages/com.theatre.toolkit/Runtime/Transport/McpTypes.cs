using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Theatre.Transport
{
    // --- Initialize ---

    /// <summary>
    /// Parameters for the MCP initialize request.
    /// </summary>
    public sealed class McpInitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonPropertyName("capabilities")]
        public JsonElement? Capabilities { get; set; }

        [JsonPropertyName("clientInfo")]
        public McpImplementationInfo ClientInfo { get; set; }
    }

    /// <summary>
    /// Result for the MCP initialize response.
    /// </summary>
    public sealed class McpInitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonPropertyName("capabilities")]
        public McpServerCapabilities Capabilities { get; set; }

        [JsonPropertyName("serverInfo")]
        public McpImplementationInfo ServerInfo { get; set; }

        [JsonPropertyName("instructions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Instructions { get; set; }
    }

    /// <summary>
    /// Name and version of an MCP implementation (client or server).
    /// </summary>
    public sealed class McpImplementationInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// Server capability declarations sent during initialization.
    /// </summary>
    public sealed class McpServerCapabilities
    {
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpToolCapability Tools { get; set; }

        [JsonPropertyName("logging")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Logging { get; set; }
    }

    /// <summary>
    /// Declares tool-related capabilities.
    /// </summary>
    public sealed class McpToolCapability
    {
        [JsonPropertyName("listChanged")]
        public bool ListChanged { get; set; }
    }

    // --- Tools ---

    /// <summary>
    /// MCP tool definition as returned in tools/list.
    /// </summary>
    public sealed class McpToolDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Description { get; set; }

        [JsonPropertyName("inputSchema")]
        public JsonElement InputSchema { get; set; }

        [JsonPropertyName("annotations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpToolAnnotations Annotations { get; set; }
    }

    /// <summary>
    /// Optional tool annotations providing hints to MCP clients.
    /// </summary>
    public sealed class McpToolAnnotations
    {
        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Title { get; set; }

        [JsonPropertyName("readOnlyHint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ReadOnlyHint { get; set; }
    }

    /// <summary>
    /// Result envelope for tools/list.
    /// </summary>
    public sealed class McpToolsListResult
    {
        [JsonPropertyName("tools")]
        public List<McpToolDefinition> Tools { get; set; } = new();
    }

    /// <summary>
    /// Parameters for tools/call.
    /// </summary>
    public sealed class McpToolCallParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Arguments { get; set; }
    }

    /// <summary>
    /// Result envelope for tools/call.
    /// </summary>
    public sealed class McpToolCallResult
    {
        [JsonPropertyName("content")]
        public List<McpContentItem> Content { get; set; } = new();

        [JsonPropertyName("isError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsError { get; set; }
    }

    /// <summary>
    /// A single content item in a tools/call result.
    /// </summary>
    public sealed class McpContentItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }
    }
}
