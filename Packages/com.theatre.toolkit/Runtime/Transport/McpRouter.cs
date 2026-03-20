using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Theatre;

namespace Theatre.Transport
{
    /// <summary>
    /// Routes MCP JSON-RPC requests to the appropriate handler.
    /// Handles initialize, tools/list, tools/call, and notifications.
    /// </summary>
    public sealed class McpRouter
    {
        private readonly ToolRegistry _registry;
        private readonly Func<ToolGroup> _getEnabledGroups;
        private readonly Func<System.Collections.Generic.HashSet<string>> _getDisabledTools;
        private readonly Func<string, JsonElement?, string> _executeToolOnMainThread;

        private string _sessionId;
        private bool _initialized;
        private McpImplementationInfo _clientInfo;

        /// <summary>Whether a client has completed the initialize handshake.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>Current session ID, or null if no session.</summary>
        public string SessionId => _sessionId;

        /// <summary>Connected client info, or null.</summary>
        public McpImplementationInfo ClientInfo => _clientInfo;

        /// <param name="registry">Tool registry to query.</param>
        /// <param name="getEnabledGroups">Returns current enabled groups.</param>
        /// <param name="getDisabledTools">Returns current disabled tools set.</param>
        /// <param name="executeToolOnMainThread">
        /// Dispatches tool execution to the main thread.
        /// (toolName, arguments) => JSON result string.
        /// </param>
        public McpRouter(
            ToolRegistry registry,
            Func<ToolGroup> getEnabledGroups,
            Func<System.Collections.Generic.HashSet<string>> getDisabledTools,
            Func<string, JsonElement?, string> executeToolOnMainThread)
        {
            _registry = registry;
            _getEnabledGroups = getEnabledGroups;
            _getDisabledTools = getDisabledTools;
            _executeToolOnMainThread = executeToolOnMainThread;
        }

        /// <summary>
        /// Handle an incoming POST /mcp request.
        /// Reads the body, parses JSON-RPC, routes, and writes the response.
        /// Called from a background thread.
        /// </summary>
        public void HandlePost(HttpListenerContext context)
        {
            string body;
            using (var reader = new StreamReader(
                context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            // Validate session on non-initialize requests
            var sessionHeader = context.Request.Headers["Mcp-Session-Id"];

            JsonRpcMessage message;
            try
            {
                message = JsonSerializer.Deserialize<JsonRpcMessage>(body);
            }
            catch (JsonException ex)
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    null, JsonRpcResponse.ParseError,
                    $"Parse error: {ex.Message}"));
                return;
            }

            if (message == null)
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    null, JsonRpcResponse.InvalidRequest, "Empty message"));
                return;
            }

            // Route by method
            if (message.IsNotification)
            {
                HandleNotification(context, message);
            }
            else if (message.IsRequest)
            {
                // Session validation (skip for initialize)
                if (message.Method != "initialize" && _initialized
                    && sessionHeader != _sessionId)
                {
                    context.Response.StatusCode = 400;
                    SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                        message.Id, JsonRpcResponse.InvalidRequest,
                        "Invalid or missing Mcp-Session-Id"));
                    return;
                }

                HandleRequest(context, message);
            }
            else
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    message.Id, JsonRpcResponse.InvalidRequest,
                    "Message must be a request or notification"));
            }
        }

        private void HandleNotification(
            HttpListenerContext context, JsonRpcMessage message)
        {
            switch (message.Method)
            {
                case "notifications/initialized":
                    // Client acknowledges initialization
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    break;

                case "notifications/cancelled":
                    // Cancellation — acknowledge
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    break;

                default:
                    // Unknown notification — accept silently per spec
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    break;
            }
        }

        private void HandleRequest(
            HttpListenerContext context, JsonRpcMessage message)
        {
            switch (message.Method)
            {
                case "initialize":
                    HandleInitialize(context, message);
                    break;

                case "tools/list":
                    HandleToolsList(context, message);
                    break;

                case "tools/call":
                    HandleToolsCall(context, message);
                    break;

                case "ping":
                    SendJsonResponse(context, JsonRpcResponse.Success(
                        message.Id, JsonDocument.Parse("{}").RootElement));
                    break;

                default:
                    SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                        message.Id, JsonRpcResponse.MethodNotFound,
                        $"Unknown method: {message.Method}"));
                    break;
            }
        }

        private void HandleInitialize(
            HttpListenerContext context, JsonRpcMessage message)
        {
            // Parse client info
            if (message.Params.HasValue)
            {
                try
                {
                    var initParams = JsonSerializer.Deserialize<McpInitializeParams>(
                        message.Params.Value.GetRawText());
                    _clientInfo = initParams?.ClientInfo;
                }
                catch { /* best effort */ }
            }

            _sessionId = Guid.NewGuid().ToString();
            _initialized = true;

            var result = new McpInitializeResult
            {
                ProtocolVersion = TheatreConfig.ProtocolVersion,
                Capabilities = new McpServerCapabilities
                {
                    Tools = new McpToolCapability { ListChanged = true }
                },
                ServerInfo = new McpImplementationInfo
                {
                    Name = "theatre",
                    Version = TheatreConfig.ServerVersion
                },
                Instructions = "Theatre gives AI agents spatial awareness of "
                    + "running Unity games and programmatic control over Unity "
                    + "subsystems. Use tools/list to see available tools."
            };

            var resultJson = JsonSerializer.SerializeToElement(result);
            var response = JsonRpcResponse.Success(message.Id, resultJson);

            context.Response.Headers.Set("Mcp-Session-Id", _sessionId);
            SendJsonResponse(context, response);

            UnityEngine.Debug.Log(
                $"[Theatre] MCP client connected: "
                + $"{_clientInfo?.Name ?? "unknown"} "
                + $"{_clientInfo?.Version ?? ""}");
        }

        private void HandleToolsList(
            HttpListenerContext context, JsonRpcMessage message)
        {
            var tools = _registry.ListTools(
                _getEnabledGroups(), _getDisabledTools());

            var listResult = new McpToolsListResult { Tools = tools };
            var resultJson = JsonSerializer.SerializeToElement(listResult);

            SendJsonResponse(context, JsonRpcResponse.Success(
                message.Id, resultJson));
        }

        private void HandleToolsCall(
            HttpListenerContext context, JsonRpcMessage message)
        {
            McpToolCallParams callParams;
            try
            {
                callParams = JsonSerializer.Deserialize<McpToolCallParams>(
                    message.Params.Value.GetRawText());
            }
            catch (Exception ex)
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    message.Id, JsonRpcResponse.InvalidParams,
                    $"Invalid tool call params: {ex.Message}"));
                return;
            }

            if (string.IsNullOrEmpty(callParams?.Name))
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    message.Id, JsonRpcResponse.InvalidParams,
                    "Missing tool name"));
                return;
            }

            // Check tool exists and is enabled
            var tool = _registry.GetTool(
                callParams.Name, _getEnabledGroups(), _getDisabledTools());
            if (tool == null)
            {
                SendJsonResponse(context, JsonRpcResponse.ErrorResponse(
                    message.Id, JsonRpcResponse.InvalidParams,
                    $"Unknown tool: {callParams.Name}"));
                return;
            }

            // Execute on main thread
            try
            {
                var resultJson = _executeToolOnMainThread(
                    callParams.Name, callParams.Arguments);

                var callResult = new McpToolCallResult
                {
                    Content = new System.Collections.Generic.List<McpContentItem>
                    {
                        new McpContentItem
                        {
                            Type = "text",
                            Text = resultJson
                        }
                    },
                    IsError = false
                };

                var resultElement = JsonSerializer.SerializeToElement(callResult);
                SendJsonResponse(context, JsonRpcResponse.Success(
                    message.Id, resultElement));
            }
            catch (Exception ex)
            {
                var errorResult = new McpToolCallResult
                {
                    Content = new System.Collections.Generic.List<McpContentItem>
                    {
                        new McpContentItem
                        {
                            Type = "text",
                            Text = ex.Message
                        }
                    },
                    IsError = true
                };

                var resultElement = JsonSerializer.SerializeToElement(errorResult);
                SendJsonResponse(context, JsonRpcResponse.Success(
                    message.Id, resultElement));
            }
        }

        private static void SendJsonResponse(
            HttpListenerContext context, JsonRpcMessage response)
        {
            var json = JsonSerializer.Serialize(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
    }
}
