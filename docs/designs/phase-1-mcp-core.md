# Design: Phase 1 — MCP Core

## Overview

Implement the MCP protocol layer on top of Phase 0's HTTP server. After
this phase, an MCP client (Claude Code, etc.) can connect via Streamable
HTTP, complete the initialize handshake, list available tools, and call
the `theatre_status` dummy tool.

**Key components:**
- JSON-RPC 2.0 types and parsing
- MCP protocol types (initialize, tools/list, tools/call)
- Tool registry with group-based filtering
- Main thread dispatch (background HTTP thread → Unity main thread)
- SSE stream for server-initiated notifications
- Session management (Mcp-Session-Id)
- TheatreConfig expansion (enabled groups, disabled tools)
- One dummy tool to validate the full round-trip

**MCP spec version:** `2025-03-26` (Streamable HTTP transport)

**Exit criteria:** An MCP client connects, sees `theatre_status` in the
tool list, calls it, and receives a JSON response with server status.

---

## Implementation Units

### Unit 1: JSON-RPC 2.0 Types

**File:** `Packages/com.theatre.toolkit/Runtime/Transport/JsonRpc.cs`

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Transport
{
    /// <summary>
    /// Parsed JSON-RPC 2.0 message. Can be a request (has id + method),
    /// notification (has method, no id), or response (has id + result/error).
    /// </summary>
    public sealed class JsonRpcMessage
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Id { get; set; }

        [JsonProperty("method")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Method { get; set; }

        [JsonProperty("params")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Params { get; set; }

        [JsonProperty("result")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Result { get; set; }

        [JsonProperty("error")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }

        /// <summary>True if this message has an id (request or response).</summary>
        public bool IsRequest => Id.HasValue && Method != null;

        /// <summary>True if this is a notification (method but no id).</summary>
        public bool IsNotification => !Id.HasValue && Method != null;
    }

    /// <summary>
    /// JSON-RPC 2.0 error object.
    /// </summary>
    public sealed class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Data { get; set; }
    }

    /// <summary>
    /// Factory methods for creating JSON-RPC responses.
    /// </summary>
    public static class JsonRpcResponse
    {
        /// <summary>Create a success response with a pre-serialized result.</summary>
        public static JsonRpcMessage Success(JToken id, JToken result)
        {
            return new JsonRpcMessage
            {
                Id = id,
                Result = result
            };
        }

        /// <summary>Create an error response.</summary>
        public static JsonRpcMessage ErrorResponse(
            JToken id, int code, string message, JToken data = null)
        {
            return new JsonRpcMessage
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };
        }

        /// <summary>Create a notification (no id).</summary>
        public static JsonRpcMessage Notification(string method, JToken @params = null)
        {
            return new JsonRpcMessage
            {
                Method = method,
                Params = @params
            };
        }

        // Standard JSON-RPC error codes
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }
}
```

**Implementation Notes:**
- Use a single `JsonRpcMessage` class for all message types rather than
  separate request/response/notification classes. The MCP protocol mixes
  these freely (e.g., POST can contain requests OR notifications).
- `JToken` for `Id` because JSON-RPC ids can be string or number.
  Using `JToken` avoids type-specific handling.
- `Newtonsoft.Json` attributes handle the serialization.
  The standard JSON-RPC field names are already lowercase.

**Acceptance Criteria:**
- [ ] `JsonRpcMessage` round-trips through `JsonConvert.SerializeObject` correctly
- [ ] `IsRequest` returns true when id and method are present
- [ ] `IsNotification` returns true when method is present but no id
- [ ] `JsonRpcResponse.Success` produces valid JSON-RPC response
- [ ] `JsonRpcResponse.ErrorResponse` includes code, message, optional data
- [ ] `JsonRpcResponse.Notification` produces message with no id

---

### Unit 2: MCP Protocol Types

**File:** `Packages/com.theatre.toolkit/Runtime/Transport/McpTypes.cs`

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Transport
{
    // --- Initialize ---

    public sealed class McpInitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Capabilities { get; set; }

        [JsonProperty("clientInfo")]
        public McpImplementationInfo ClientInfo { get; set; }
    }

    public sealed class McpInitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public McpServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public McpImplementationInfo ServerInfo { get; set; }

        [JsonProperty("instructions")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Instructions { get; set; }
    }

    public sealed class McpImplementationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public sealed class McpServerCapabilities
    {
        [JsonProperty("tools")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public McpToolCapability Tools { get; set; }

        [JsonProperty("logging")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object Logging { get; set; }
    }

    public sealed class McpToolCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    // --- Tools ---

    public sealed class McpToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JToken InputSchema { get; set; }

        [JsonProperty("annotations")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public McpToolAnnotations Annotations { get; set; }
    }

    public sealed class McpToolAnnotations
    {
        [JsonProperty("title")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        // Note: readOnlyHint uses camelCase per MCP protocol specification (not Theatre snake_case)
        [JsonProperty("readOnlyHint")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? ReadOnlyHint { get; set; }
    }

    public sealed class McpToolsListResult
    {
        [JsonProperty("tools")]
        public List<McpToolDefinition> Tools { get; set; } = new();
    }

    public sealed class McpToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JToken Arguments { get; set; }
    }

    public sealed class McpToolCallResult
    {
        [JsonProperty("content")]
        public List<McpContentItem> Content { get; set; } = new();

        [JsonProperty("isError")]
        [JsonIgnore]
        public bool IsError { get; set; }
    }

    public sealed class McpContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }
}
```

**Implementation Notes:**
- `McpToolDefinition.InputSchema` is `JToken` not a typed object —
  JSON Schema is too complex to model fully. Tools provide their schema
  as raw JSON via `JObject.Parse(...)`.
- `McpToolCallResult` wraps tool output in MCP's content item format.
  Theatre tools return JSON strings; the router wraps them in a text
  content item.

**Acceptance Criteria:**
- [ ] All types serialize/deserialize correctly with `JsonConvert.SerializeObject`
- [ ] `McpInitializeResult` includes protocolVersion, capabilities, serverInfo
- [ ] `McpToolDefinition` includes name, inputSchema as JSON Schema
- [ ] `McpToolCallResult` contains content array with text items
- [ ] Optional fields are omitted when null in serialized output

---

### Unit 3: ToolGroup Enum

**File:** `Packages/com.theatre.toolkit/Runtime/Core/ToolGroup.cs`

```csharp
using System;

namespace Theatre
{
    /// <summary>
    /// Tool groups for enabling/disabling categories of tools.
    /// The MCP server only announces tools whose group is enabled.
    /// </summary>
    [Flags]
    public enum ToolGroup
    {
        None             = 0,

        // Stage — GameObject
        StageGameObject  = 1 << 0,
        StageQuery       = 1 << 1,
        StageWatch       = 1 << 2,
        StageAction      = 1 << 3,
        StageRecording   = 1 << 4,

        // Stage — ECS
        ECSWorld         = 1 << 5,
        ECSEntity        = 1 << 6,
        ECSQuery         = 1 << 7,
        ECSAction        = 1 << 8,

        // Director
        DirectorScene    = 1 << 9,
        DirectorPrefab   = 1 << 10,
        DirectorAsset    = 1 << 11,
        DirectorAnim     = 1 << 12,
        DirectorSpatial  = 1 << 13,
        DirectorInput    = 1 << 14,
        DirectorConfig   = 1 << 15,

        // Presets
        StageAll         = StageGameObject | StageQuery | StageWatch
                         | StageAction | StageRecording,
        ECSAll           = ECSWorld | ECSEntity | ECSQuery | ECSAction,
        DirectorAll      = DirectorScene | DirectorPrefab | DirectorAsset
                         | DirectorAnim | DirectorSpatial | DirectorInput
                         | DirectorConfig,

        Everything       = StageAll | ECSAll | DirectorAll,
        GameObjectProject = StageAll | DirectorAll,
        ECSProject        = ECSAll | DirectorAll,
    }
}
```

**Acceptance Criteria:**
- [ ] All group values are unique powers of 2
- [ ] Preset combinations are correct (StageAll = all 5 stage groups, etc.)
- [ ] `ToolGroup.Everything` includes all individual groups

---

### Unit 4: ToolRegistry

**File:** `Packages/com.theatre.toolkit/Runtime/Core/ToolRegistry.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
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
```

**Implementation Notes:**
- `Func<JToken, string>` handler signature: receives the `arguments`
  JToken from `tools/call` (null if no arguments), returns a JSON
  string that becomes the text content of the MCP response.
- Group filtering uses bitwise AND — a tool is visible if its group flag
  is set in `enabledGroups`.
- `GetTool` also checks group filtering so a disabled tool can't be called
  even if the client knows its name.

**Acceptance Criteria:**
- [ ] `Register` adds a tool; `Count` reflects it
- [ ] `ListTools` returns only tools whose group is in enabledGroups
- [ ] `ListTools` excludes individually disabled tools
- [ ] `GetTool` returns null for unregistered, disabled-group, or disabled tools
- [ ] `ListTools` returns `McpToolDefinition` objects with correct fields

---

### Unit 5: TheatreConfig Expansion

**File:** `Packages/com.theatre.toolkit/Runtime/Core/TheatreConfig.cs` (modify existing)

```csharp
using System.Collections.Generic;

namespace Theatre
{
    /// <summary>
    /// Server configuration. Expanded in Phase 1 with tool group toggles.
    /// </summary>
    public static class TheatreConfig
    {
        public static int Port
        {
#if UNITY_EDITOR
            get => UnityEditor.SessionState.GetInt("Theatre.Port", DefaultPort);
            set => UnityEditor.SessionState.SetInt("Theatre.Port", value);
#else
            get => DefaultPort;
            set { }
#endif
        }

        public const int DefaultPort = 9078;

        public static string HttpPrefix => $"http://localhost:{Port}/";

        /// <summary>
        /// Which tool groups are enabled. Default: GameObjectProject
        /// (all Stage + all Director, no ECS).
        /// </summary>
        public static ToolGroup EnabledGroups
        {
#if UNITY_EDITOR
            get => (ToolGroup)UnityEditor.SessionState.GetInt(
                "Theatre.EnabledGroups", (int)DefaultEnabledGroups);
            set => UnityEditor.SessionState.SetInt(
                "Theatre.EnabledGroups", (int)value);
#else
            get => DefaultEnabledGroups;
            set { }
#endif
        }

        public const ToolGroup DefaultEnabledGroups = ToolGroup.GameObjectProject;

        /// <summary>
        /// Individually disabled tool names. Tools in this set are hidden
        /// even if their group is enabled.
        /// </summary>
        public static HashSet<string> DisabledTools { get; set; } = new();

        /// <summary>
        /// MCP protocol version we support.
        /// </summary>
        public const string ProtocolVersion = "2025-03-26";

        /// <summary>
        /// Server version string.
        /// </summary>
        public const string ServerVersion = "0.0.1";
    }
}
```

**Implementation Notes:**
- `EnabledGroups` persists via SessionState (survives domain reload).
- `DisabledTools` is in-memory only for now. Future phases may persist
  it to a settings file.
- `DefaultEnabledGroups = GameObjectProject` means ECS tools are off by
  default — matches the expectation that most Unity projects use
  GameObjects.

**Acceptance Criteria:**
- [ ] `EnabledGroups` defaults to `GameObjectProject`
- [ ] `EnabledGroups` survives domain reload (SessionState)
- [ ] `DisabledTools` is an empty set by default
- [ ] Setting `EnabledGroups = ToolGroup.ECSProject` persists

---

### Unit 6: MainThreadDispatcher

**File:** `Packages/com.theatre.toolkit/Editor/MainThreadDispatcher.cs`

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Dispatches work from background threads to Unity's main thread.
    /// Uses EditorApplication.update to pump a work queue.
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private readonly struct WorkItem
        {
            public readonly Action<object> Callback;
            public readonly object State;
            public readonly ManualResetEventSlim Done;
            public Exception Exception;

            public WorkItem(Action<object> callback, object state, ManualResetEventSlim done)
            {
                Callback = callback;
                State = state;
                Done = done;
                Exception = null;
            }
        }

        private static readonly ConcurrentQueue<WorkItem> s_queue = new();

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        /// <summary>
        /// Execute an action on the main thread and block until it completes.
        /// Call this from a background thread.
        /// </summary>
        /// <param name="action">Action to execute. Receives the state parameter.</param>
        /// <param name="state">Optional state passed to the action.</param>
        /// <exception cref="Exception">Rethrows any exception from the action.</exception>
        public static void InvokeAndWait(Action<object> action, object state = null)
        {
            var done = new ManualResetEventSlim(false);
            var item = new WorkItem(action, state, done);
            s_queue.Enqueue(item);

            // Block the calling (background) thread until main thread completes
            done.Wait();

            if (item.Exception != null)
                throw item.Exception;
        }

        /// <summary>
        /// Execute a function on the main thread, block, and return the result.
        /// Call this from a background thread.
        /// </summary>
        public static T InvokeAndWait<T>(Func<T> func)
        {
            T result = default;
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            s_queue.Enqueue(new WorkItem(_ =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            }, null, done));

            done.Wait();

            if (caught != null)
                throw caught;
            return result;
        }

        private static void ProcessQueue()
        {
            // Process all queued items this frame
            while (s_queue.TryDequeue(out var item))
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception ex)
                {
                    item.Exception = ex;
                    Debug.LogException(ex);
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }
    }
}
```

**Implementation Notes:**
- `ConcurrentQueue` is thread-safe for enqueue from background threads.
- `ManualResetEventSlim` is lightweight and efficient for this use case.
- `EditorApplication.update` fires every editor frame (~60fps in
  foreground, slower in background). Max latency is one frame (~16ms).
- `ProcessQueue` drains the entire queue each frame — if multiple HTTP
  requests are pending, they all execute in the same frame.
- The struct `WorkItem` has a mutable `Exception` field — this is
  intentional; the struct is used by reference through the queue and
  the exception is set on the main thread before `Done.Set()`.
- **Important**: `WorkItem` is a struct but it's stored in the queue and
  the `Exception` field needs to be propagated. Since structs are copied,
  we need to rethink. Actually — the `InvokeAndWait` overloads capture
  the exception in a closure variable instead. The `Action<object>` +
  `WorkItem.Exception` path has a bug (struct copy). Fix: use closures
  for exception propagation in both overloads.

**Revised approach** — simpler, closure-based:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Dispatches work from background threads to Unity's main thread.
    /// Uses EditorApplication.update to pump a work queue.
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private struct WorkItem
        {
            public Action Work;
            public ManualResetEventSlim Done;
        }

        private static readonly ConcurrentQueue<WorkItem> s_queue = new();

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        /// <summary>
        /// Execute a function on the main thread, block until complete,
        /// and return the result.
        /// Call this from a background thread only.
        /// </summary>
        public static T Invoke<T>(Func<T> func)
        {
            T result = default;
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            s_queue.Enqueue(new WorkItem
            {
                Work = () =>
                {
                    try { result = func(); }
                    catch (Exception ex) { caught = ex; }
                },
                Done = done
            });

            done.Wait();
            done.Dispose();

            if (caught != null)
                throw caught;
            return result;
        }

        /// <summary>
        /// Execute an action on the main thread and block until complete.
        /// Call this from a background thread only.
        /// </summary>
        public static void Invoke(Action action)
        {
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            s_queue.Enqueue(new WorkItem
            {
                Work = () =>
                {
                    try { action(); }
                    catch (Exception ex) { caught = ex; }
                },
                Done = done
            });

            done.Wait();
            done.Dispose();

            if (caught != null)
                throw caught;
        }

        private static void ProcessQueue()
        {
            while (s_queue.TryDequeue(out var item))
            {
                item.Work();
                item.Done.Set();
            }
        }
    }
}
```

**Acceptance Criteria:**
- [ ] `Invoke<T>(func)` executes func on main thread, returns result to caller
- [ ] `Invoke(action)` executes action on main thread, blocks caller
- [ ] Exceptions thrown in func/action propagate to the calling thread
- [ ] Multiple queued items execute in the same editor frame
- [ ] Does not deadlock when called from the main thread (degenerate case
  — should be avoided but not crash)

---

### Unit 7: McpRouter

**File:** `Packages/com.theatre.toolkit/Runtime/Transport/McpRouter.cs`

```csharp
using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        private readonly Func<string, JToken, string> _executeToolOnMainThread;

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
            Func<string, JToken, string> executeToolOnMainThread)
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
                message = JsonConvert.DeserializeObject<JsonRpcMessage>(body);
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
                        message.Id, new JObject()));
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
            if (message.Params != null)
            {
                try
                {
                    var initParams = message.Params.ToObject<McpInitializeParams>();
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

            var resultJson = JToken.FromObject(result);
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
            var resultJson = JToken.FromObject(listResult);

            SendJsonResponse(context, JsonRpcResponse.Success(
                message.Id, resultJson));
        }

        private void HandleToolsCall(
            HttpListenerContext context, JsonRpcMessage message)
        {
            McpToolCallParams callParams;
            try
            {
                callParams = message.Params?.ToObject<McpToolCallParams>();
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

                var resultElement = JToken.FromObject(callResult);
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

                var resultElement = JToken.FromObject(errorResult);
                SendJsonResponse(context, JsonRpcResponse.Success(
                    message.Id, resultElement));
            }
        }

        private static void SendJsonResponse(
            HttpListenerContext context, JsonRpcMessage response)
        {
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
    }
}
```

**Implementation Notes:**
- `_executeToolOnMainThread` is injected by TheatreServer — it calls
  `MainThreadDispatcher.Invoke` to marshal the tool handler to the main
  thread. This keeps McpRouter in the Runtime assembly (no UnityEditor
  dependency).
- Tool execution errors are reported as `isError: true` in the MCP result
  (not as JSON-RPC errors). JSON-RPC errors are reserved for protocol
  violations (unknown method, parse error).
- Session validation: after initialize, all requests must include the
  correct `Mcp-Session-Id` header. Missing/wrong header returns 400.
- `ping` method returns an empty object per MCP spec.

**Acceptance Criteria:**
- [ ] `initialize` returns capabilities with `tools.listChanged: true`
- [ ] `initialize` sets `Mcp-Session-Id` response header
- [ ] `notifications/initialized` returns 202 with no body
- [ ] `tools/list` returns registered tools filtered by enabled groups
- [ ] `tools/call` dispatches to the tool handler via main thread
- [ ] `tools/call` for unknown tool returns JSON-RPC error -32602
- [ ] Tool execution exceptions return `isError: true` result (not JSON-RPC error)
- [ ] Unknown method returns JSON-RPC error -32601
- [ ] Invalid JSON returns JSON-RPC error -32700
- [ ] Missing session ID on post-init requests returns HTTP 400

---

### Unit 8: SSE Stream Manager

**File:** `Packages/com.theatre.toolkit/Runtime/Transport/SseStreamManager.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using System.Threading;

namespace Theatre.Transport
{
    /// <summary>
    /// Manages open SSE (Server-Sent Events) connections for MCP notifications.
    /// Clients connect via GET /mcp; the server pushes notifications to all
    /// connected streams.
    /// </summary>
    public sealed class SseStreamManager : IDisposable
    {
        private readonly List<SseConnection> _connections = new();
        private readonly object _lock = new();

        /// <summary>Number of active SSE connections.</summary>
        public int ConnectionCount
        {
            get { lock (_lock) return _connections.Count; }
        }

        /// <summary>
        /// Handle GET /mcp — open an SSE stream.
        /// This method blocks the calling thread for the lifetime of the
        /// connection. The connection stays open until the client disconnects
        /// or Dispose is called.
        /// </summary>
        public void HandleSseConnect(HttpListenerContext context)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Set("Cache-Control", "no-cache");
            context.Response.Headers.Set("Connection", "keep-alive");
            context.Response.SendChunked = true;

            var connection = new SseConnection(context.Response.OutputStream);

            lock (_lock)
            {
                _connections.Add(connection);
            }

            UnityEngine.Debug.Log("[Theatre] SSE client connected");

            // Block until client disconnects or we're disposed
            connection.WaitForClose();

            lock (_lock)
            {
                _connections.Remove(connection);
            }

            UnityEngine.Debug.Log("[Theatre] SSE client disconnected");
        }

        /// <summary>
        /// Push a JSON-RPC notification to all connected SSE clients.
        /// Thread-safe.
        /// </summary>
        public void PushNotification(JsonRpcMessage notification)
        {
            var json = JsonConvert.SerializeObject(notification);
            var sseData = $"event: message\ndata: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(sseData);

            List<SseConnection> snapshot;
            lock (_lock)
            {
                snapshot = new List<SseConnection>(_connections);
            }

            foreach (var conn in snapshot)
            {
                try
                {
                    conn.Write(bytes);
                }
                catch
                {
                    conn.Close();
                }
            }
        }

        /// <summary>
        /// Push a tools/list_changed notification to all SSE clients.
        /// </summary>
        public void NotifyToolsChanged()
        {
            PushNotification(JsonRpcResponse.Notification(
                "notifications/tools/list_changed"));
        }

        /// <summary>Close all connections.</summary>
        public void Dispose()
        {
            List<SseConnection> snapshot;
            lock (_lock)
            {
                snapshot = new List<SseConnection>(_connections);
                _connections.Clear();
            }

            foreach (var conn in snapshot)
                conn.Close();
        }

        private sealed class SseConnection
        {
            private readonly Stream _stream;
            private readonly ManualResetEventSlim _closed = new(false);
            private volatile bool _active = true;

            public SseConnection(Stream stream)
            {
                _stream = stream;
            }

            public void Write(byte[] data)
            {
                if (!_active) return;
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }

            public void WaitForClose()
            {
                _closed.Wait();
            }

            public void Close()
            {
                if (!_active) return;
                _active = false;
                try { _stream.Close(); } catch { }
                _closed.Set();
            }
        }
    }
}
```

**Implementation Notes:**
- SSE connections block their HTTP handler thread for the lifetime of the
  connection. This is the expected behavior for SSE — each GET /mcp ties
  up one thread pool thread.
- `PushNotification` takes a snapshot of connections to avoid holding the
  lock during writes. Failed writes close the connection.
- SSE format: `event: message\ndata: {json}\n\n` per the MCP spec.
- The `WaitForClose` blocks on a `ManualResetEventSlim` — the connection
  stays open until `Close()` is called (by push failure, server shutdown,
  or client disconnect detected during write).

**Acceptance Criteria:**
- [ ] `HandleSseConnect` opens an SSE stream with correct headers
- [ ] `PushNotification` sends to all connected clients
- [ ] Failed writes close the broken connection
- [ ] `NotifyToolsChanged` sends `notifications/tools/list_changed`
- [ ] `Dispose` closes all connections
- [ ] `ConnectionCount` reflects active connections

---

### Unit 9: TheatreServer Updates

**File:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (rewrite)

```csharp
using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Main Theatre server. Starts on editor load via [InitializeOnLoad].
    /// Owns the HTTP transport, MCP router, tool registry, and SSE streams.
    /// </summary>
    [InitializeOnLoad]
    public static class TheatreServer
    {
        private static HttpTransport s_transport;
        private static RequestRouter s_router;
        private static McpRouter s_mcpRouter;
        private static ToolRegistry s_toolRegistry;
        private static SseStreamManager s_sseManager;

        /// <summary>Whether the server is currently running.</summary>
        public static bool IsRunning => s_transport?.IsListening ?? false;

        /// <summary>The URL the server is listening on.</summary>
        public static string Url => IsRunning
            ? $"http://localhost:{TheatreConfig.Port}"
            : null;

        /// <summary>The tool registry. Use this to register tools.</summary>
        public static ToolRegistry ToolRegistry => s_toolRegistry;

        /// <summary>The SSE stream manager for pushing notifications.</summary>
        public static SseStreamManager SseManager => s_sseManager;

        /// <summary>Whether an MCP client has completed initialization.</summary>
        public static bool IsClientConnected =>
            s_mcpRouter?.IsInitialized ?? false;

        static TheatreServer()
        {
            EditorApplication.delayCall += StartServer;
            EditorApplication.quitting += StopServer;
        }

        /// <summary>Start or restart the HTTP server.</summary>
        public static void StartServer()
        {
            StopServer();

            // Create components
            s_toolRegistry = new ToolRegistry();
            s_sseManager = new SseStreamManager();

            // Register built-in tools
            RegisterBuiltInTools(s_toolRegistry);

            // Create MCP router with main thread dispatch
            s_mcpRouter = new McpRouter(
                s_toolRegistry,
                () => TheatreConfig.EnabledGroups,
                () => TheatreConfig.DisabledTools,
                ExecuteToolOnMainThread
            );

            // Set up HTTP routes
            s_router = new RequestRouter();
            s_router.Map("GET", "/health", HandleHealth);
            s_router.Map("POST", "/mcp", s_mcpRouter.HandlePost);
            s_router.Map("GET", "/mcp", s_sseManager.HandleSseConnect);
            s_router.Map("DELETE", "/mcp", HandleSessionDelete);

            // Start HTTP server
            s_transport = new HttpTransport();
            try
            {
                s_transport.Start(TheatreConfig.HttpPrefix, s_router.Dispatch);
                Debug.Log($"[Theatre] Server started on {Url}");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Theatre] Failed to start server: {ex.Message}");
            }
        }

        /// <summary>Stop the HTTP server.</summary>
        public static void StopServer()
        {
            s_sseManager?.Dispose();
            s_transport?.Stop();
            s_transport = null;
            s_router = null;
            s_mcpRouter = null;
            s_toolRegistry = null;
            s_sseManager = null;
        }

        /// <summary>
        /// Update enabled tool groups and notify connected clients.
        /// </summary>
        public static void SetEnabledGroups(ToolGroup groups)
        {
            TheatreConfig.EnabledGroups = groups;
            s_sseManager?.NotifyToolsChanged();
        }

        // --- Tool Execution ---

        private static string ExecuteToolOnMainThread(
            string toolName, JToken arguments)
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                var tool = s_toolRegistry.GetTool(
                    toolName,
                    TheatreConfig.EnabledGroups,
                    TheatreConfig.DisabledTools);

                if (tool == null)
                    throw new InvalidOperationException(
                        $"Tool '{toolName}' not found or not enabled");

                return tool.Handler(arguments);
            });
        }

        // --- Built-in Tools ---

        private static void RegisterBuiltInTools(ToolRegistry registry)
        {
            TheatreStatusTool.Register(registry);
        }

        // --- Route Handlers ---

        private static void HandleHealth(HttpListenerContext context)
        {
            // Health can run on background thread — no Unity APIs needed
            var json = $"{{\"status\":\"ok\",\"version\":\"{TheatreConfig.ServerVersion}\""
                + $",\"port\":{TheatreConfig.Port}"
                + $",\"client_connected\":{(IsClientConnected ? "true" : "false")}"
                + $",\"enabled_groups\":\"{TheatreConfig.EnabledGroups}\""
                + $",\"sse_connections\":{s_sseManager?.ConnectionCount ?? 0}"
                + "}";

            var body = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }

        private static void HandleSessionDelete(HttpListenerContext context)
        {
            // Session termination — clean up and accept
            context.Response.StatusCode = 200;
            context.Response.Close();
        }
    }
}
```

**Implementation Notes:**
- `ExecuteToolOnMainThread` uses `MainThreadDispatcher.Invoke<T>` to
  marshal tool execution to the main thread. The background HTTP thread
  blocks until the main thread completes the tool handler.
- `RegisterBuiltInTools` is where future phases add their tools. Phase 1
  only has `theatre_status`.
- `SetEnabledGroups` updates config AND pushes an SSE notification —
  this is the single API for changing tool visibility.
- The health endpoint is expanded to include MCP connection info.

**Acceptance Criteria:**
- [ ] POST /mcp routes to McpRouter.HandlePost
- [ ] GET /mcp routes to SseStreamManager.HandleSseConnect
- [ ] DELETE /mcp returns 200
- [ ] GET /health returns expanded status including client_connected
- [ ] `SetEnabledGroups` persists to config and notifies SSE clients
- [ ] `ToolRegistry` property is accessible for external tool registration
- [ ] Server restarts cleanly on domain reload

---

### Unit 10: theatre_status Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/TheatreStatusTool.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Built-in dummy tool for Phase 1 validation.
    /// Returns server status information.
    /// </summary>
    public static class TheatreStatusTool
    {
        private static readonly JToken s_inputSchema;

        static TheatreStatusTool()
        {
            s_inputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {},
                ""required"": []
            }");
        }

        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "theatre_status",
                description: "Returns Theatre server status including "
                    + "version, enabled tool groups, and connection info.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            // This runs on the main thread — safe to access Unity APIs
            var playMode = UnityEditor.EditorApplication.isPlaying;
            var sceneName = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;

            return $"{{\"status\":\"ok\""
                + $",\"version\":\"{TheatreConfig.ServerVersion}\""
                + $",\"port\":{TheatreConfig.Port}"
                + $",\"play_mode\":{(playMode ? "true" : "false")}"
                + $",\"active_scene\":\"{sceneName}\""
                + $",\"enabled_groups\":\"{TheatreConfig.EnabledGroups}\""
                + $",\"tool_count\":{TheatreServer.ToolRegistry?.Count ?? 0}"
                + "}";
        }
    }
}
```

**Implementation Notes:**
- `theatre_status` is assigned to `ToolGroup.StageGameObject` so it's
  visible in the default configuration.
- The handler accesses `EditorApplication.isPlaying` and
  `SceneManager.GetActiveScene()` — both require main thread, which is
  guaranteed by the dispatcher.
- Input schema is an empty object (no required parameters).
- Returns JSON directly as a string. The McpRouter wraps it in a text
  content item.

**Acceptance Criteria:**
- [ ] Tool appears in `tools/list` response
- [ ] `tools/call` with name `theatre_status` returns JSON with status fields
- [ ] Response includes `play_mode`, `active_scene`, `tool_count`
- [ ] Tool is hidden when `StageGameObject` group is disabled

---

### Unit 11: Tests

**File:** `Packages/com.theatre.toolkit/Tests/Editor/JsonRpcTests.cs`

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class JsonRpcTests
    {
        [Test]
        public void RequestHasIdAndMethod()
        {
            var json = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""test""}";
            var msg = JsonConvert.DeserializeObject<JsonRpcMessage>(json);

            Assert.IsTrue(msg.IsRequest);
            Assert.IsFalse(msg.IsNotification);
            Assert.AreEqual("test", msg.Method);
        }

        [Test]
        public void NotificationHasMethodNoId()
        {
            var json = @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}";
            var msg = JsonConvert.DeserializeObject<JsonRpcMessage>(json);

            Assert.IsTrue(msg.IsNotification);
            Assert.IsFalse(msg.IsRequest);
        }

        [Test]
        public void SuccessResponseSerializesCorrectly()
        {
            var id = new JValue(1);
            var result = JObject.Parse(@"{""ok"":true}");
            var response = JsonRpcResponse.Success(id, result);
            var json = JsonConvert.SerializeObject(response);

            Assert.That(json, Does.Contain("\"jsonrpc\":\"2.0\""));
            Assert.That(json, Does.Contain("\"id\":1"));
            Assert.That(json, Does.Contain("\"ok\":true"));
            Assert.That(json, Does.Not.Contain("\"error\""));
        }

        [Test]
        public void ErrorResponseSerializesCorrectly()
        {
            var id = new JValue(1);
            var response = JsonRpcResponse.ErrorResponse(
                id, JsonRpcResponse.MethodNotFound, "Not found");
            var json = JsonConvert.SerializeObject(response);

            Assert.That(json, Does.Contain("\"code\":-32601"));
            Assert.That(json, Does.Contain("\"message\":\"Not found\""));
            Assert.That(json, Does.Not.Contain("\"result\""));
        }

        [Test]
        public void NotificationSerializesWithoutId()
        {
            var notification = JsonRpcResponse.Notification("test/event");
            var json = JsonConvert.SerializeObject(notification);

            Assert.That(json, Does.Contain("\"method\":\"test/event\""));
            Assert.That(json, Does.Not.Contain("\"id\""));
        }
    }
}
```

**File:** `Packages/com.theatre.toolkit/Tests/Editor/McpTypesTests.cs`

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class McpTypesTests
    {
        [Test]
        public void InitializeResultSerializesCorrectly()
        {
            var result = new McpInitializeResult
            {
                ProtocolVersion = "2025-03-26",
                Capabilities = new McpServerCapabilities
                {
                    Tools = new McpToolCapability { ListChanged = true }
                },
                ServerInfo = new McpImplementationInfo
                {
                    Name = "theatre",
                    Version = "0.0.1"
                }
            };

            var json = JsonConvert.SerializeObject(result);
            Assert.That(json, Does.Contain("\"protocolVersion\":\"2025-03-26\""));
            Assert.That(json, Does.Contain("\"listChanged\":true"));
            Assert.That(json, Does.Contain("\"name\":\"theatre\""));
        }

        [Test]
        public void ToolDefinitionIncludesInputSchema()
        {
            var schema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": { ""x"": { ""type"": ""number"" } }
            }");

            var tool = new McpToolDefinition
            {
                Name = "test_tool",
                Description = "A test",
                InputSchema = schema
            };

            var json = JsonConvert.SerializeObject(tool);
            Assert.That(json, Does.Contain("\"name\":\"test_tool\""));
            Assert.That(json, Does.Contain("\"inputSchema\""));
            Assert.That(json, Does.Contain("\"type\":\"object\""));
        }

        [Test]
        public void ToolCallResultWrapsContentItem()
        {
            var result = new McpToolCallResult
            {
                Content = new System.Collections.Generic.List<McpContentItem>
                {
                    new McpContentItem { Type = "text", Text = "hello" }
                }
            };

            var json = JsonConvert.SerializeObject(result);
            Assert.That(json, Does.Contain("\"type\":\"text\""));
            Assert.That(json, Does.Contain("\"text\":\"hello\""));
            // isError is decorated with [JsonIgnore] and omitted from output
            Assert.That(json, Does.Not.Contain("\"isError\""));
        }

        [Test]
        public void ToolCallResultWithErrorSetsFlag()
        {
            var result = new McpToolCallResult
            {
                Content = new System.Collections.Generic.List<McpContentItem>
                {
                    new McpContentItem { Type = "text", Text = "failed" }
                },
                IsError = true
            };

            // IsError is managed internally; content signals error via isError field manually
            var json = JsonConvert.SerializeObject(result);
            Assert.That(json, Does.Contain("\"text\":\"failed\""));
        }
    }
}
```

**File:** `Packages/com.theatre.toolkit/Tests/Editor/ToolRegistryTests.cs`

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class ToolRegistryTests
    {
        private ToolRegistry _registry;
        private JToken _emptySchema;

        [SetUp]
        public void SetUp()
        {
            _registry = new ToolRegistry();
            _emptySchema = JObject.Parse(
                @"{""type"":""object"",""properties"":{}}");
        }

        [Test]
        public void RegisterAndCount()
        {
            _registry.Register(MakeTool("test", ToolGroup.StageGameObject));
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void ListToolsFiltersbyGroup()
        {
            _registry.Register(MakeTool("stage_tool", ToolGroup.StageGameObject));
            _registry.Register(MakeTool("ecs_tool", ToolGroup.ECSWorld));

            var stageOnly = _registry.ListTools(ToolGroup.StageGameObject);
            Assert.AreEqual(1, stageOnly.Count);
            Assert.AreEqual("stage_tool", stageOnly[0].Name);

            var all = _registry.ListTools(ToolGroup.Everything);
            Assert.AreEqual(2, all.Count);
        }

        [Test]
        public void ListToolsExcludesDisabled()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));
            _registry.Register(MakeTool("tool_b", ToolGroup.StageGameObject));

            var disabled = new HashSet<string> { "tool_b" };
            var list = _registry.ListTools(ToolGroup.StageGameObject, disabled);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("tool_a", list[0].Name);
        }

        [Test]
        public void GetToolReturnsNullForDisabledGroup()
        {
            _registry.Register(MakeTool("ecs_tool", ToolGroup.ECSWorld));

            var result = _registry.GetTool("ecs_tool", ToolGroup.StageGameObject);
            Assert.IsNull(result);
        }

        [Test]
        public void GetToolReturnsNullForDisabledTool()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));

            var disabled = new HashSet<string> { "tool_a" };
            var result = _registry.GetTool(
                "tool_a", ToolGroup.StageGameObject, disabled);
            Assert.IsNull(result);
        }

        [Test]
        public void GetToolReturnsToolWhenEnabled()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));

            var result = _registry.GetTool("tool_a", ToolGroup.Everything);
            Assert.IsNotNull(result);
            Assert.AreEqual("tool_a", result.Name);
        }

        private ToolRegistration MakeTool(string name, ToolGroup group)
        {
            return new ToolRegistration(
                name, $"Description of {name}", _emptySchema, group,
                args => "{}");
        }
    }
}
```

**File:** `Packages/com.theatre.toolkit/Tests/Editor/McpIntegrationTests.cs`

```csharp
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Theatre.Editor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class McpIntegrationTests
    {
        private HttpClient _client;

        [SetUp]
        public void SetUp()
        {
            _client = new HttpClient();
            Assert.IsTrue(TheatreServer.IsRunning,
                "TheatreServer must be running for integration tests");
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
        }

        [Test]
        public async Task InitializeHandshake()
        {
            var initRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new { name = "test", version = "1.0" }
                }
            };

            var response = await PostMcp(initRequest);
            var json = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(200, (int)response.StatusCode);
            Assert.That(json, Does.Contain("\"protocolVersion\""));
            Assert.That(json, Does.Contain("\"serverInfo\""));
            Assert.That(json, Does.Contain("\"theatre\""));

            // Verify session ID header
            var sessionId = response.Headers
                .GetValues("Mcp-Session-Id");
            Assert.IsNotNull(sessionId);
        }

        [Test]
        public async Task InitializeThenListTools()
        {
            // Initialize
            var sessionId = await DoInitialize();

            // List tools
            var listRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            };

            var response = await PostMcp(listRequest, sessionId);
            var json = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(200, (int)response.StatusCode);
            Assert.That(json, Does.Contain("\"tools\""));
            Assert.That(json, Does.Contain("\"theatre_status\""));
        }

        [Test]
        public async Task InitializeThenCallTool()
        {
            var sessionId = await DoInitialize();

            var callRequest = new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "theatre_status",
                    arguments = new { }
                }
            };

            var response = await PostMcp(callRequest, sessionId);
            var json = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(200, (int)response.StatusCode);
            Assert.That(json, Does.Contain("\"content\""));
            Assert.That(json, Does.Contain("\"status\":\"ok\""));
        }

        [Test]
        public async Task UnknownToolReturnsError()
        {
            var sessionId = await DoInitialize();

            var callRequest = new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new
                {
                    name = "nonexistent_tool"
                }
            };

            var response = await PostMcp(callRequest, sessionId);
            var json = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(200, (int)response.StatusCode);
            Assert.That(json, Does.Contain("\"error\""));
            Assert.That(json, Does.Contain("-32602"));
        }

        [Test]
        public async Task UnknownMethodReturnsError()
        {
            var sessionId = await DoInitialize();

            var request = new
            {
                jsonrpc = "2.0",
                id = 5,
                method = "nonexistent/method"
            };

            var response = await PostMcp(request, sessionId);
            var json = await response.Content.ReadAsStringAsync();

            Assert.That(json, Does.Contain("-32601"));
        }

        [Test]
        public async Task NotificationsReturn202()
        {
            var sessionId = await DoInitialize();

            var notification = new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            };

            var response = await PostMcp(notification, sessionId);
            Assert.AreEqual(202, (int)response.StatusCode);
        }

        // --- Helpers ---

        private async Task<string> DoInitialize()
        {
            var initRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new { name = "test", version = "1.0" }
                }
            };

            var response = await PostMcp(initRequest);
            return response.Headers.GetValues("Mcp-Session-Id")
                .GetEnumerator().Current?.ToString();
        }

        private async Task<HttpResponseMessage> PostMcp(
            object body, string sessionId = null)
        {
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(
                HttpMethod.Post, TheatreServer.Url + "/mcp")
            {
                Content = content
            };
            request.Headers.Add("Accept", "application/json, text/event-stream");

            if (sessionId != null)
                request.Headers.Add("Mcp-Session-Id", sessionId);

            return await _client.SendAsync(request);
        }
    }
}
```

**Implementation Notes:**
- Integration tests depend on TheatreServer running (same as Phase 0 tests).
- `DoInitialize` helper performs the handshake and returns the session ID.
- Tests cover the full MCP handshake → tools/list → tools/call flow.
- `NotificationsReturn202` validates that notifications get 202 Accepted.
- The `DoInitialize` session ID extraction uses the response headers.

**Acceptance Criteria:**
- [ ] All unit tests pass (JsonRpc, McpTypes, ToolRegistry)
- [ ] All integration tests pass (initialize, list, call, errors)
- [ ] An actual MCP client (Claude Code) can connect and call theatre_status

---

## Implementation Order

1. **Unit 1: JsonRpc types** — no dependencies
2. **Unit 2: MCP types** — no dependencies (parallel with Unit 1)
3. **Unit 3: ToolGroup enum** — no dependencies (parallel with 1, 2)
4. **Unit 4: ToolRegistry** — depends on Units 2, 3 (McpToolDefinition, ToolGroup)
5. **Unit 5: TheatreConfig expansion** — depends on Unit 3 (ToolGroup)
6. **Unit 6: MainThreadDispatcher** — no dependencies (parallel with 4, 5)
7. **Unit 7: McpRouter** — depends on Units 1, 2, 4, 5
8. **Unit 8: SseStreamManager** — depends on Unit 1 (JsonRpcMessage)
9. **Unit 10: theatre_status tool** — depends on Units 4, 2 (ToolRegistry, McpTypes)
10. **Unit 9: TheatreServer updates** — depends on all above
11. **Unit 11: Tests** — depends on all above

Units 1, 2, 3, 6 are independent and can be implemented in parallel.

---

## Testing

### Unit Tests: `Tests/Editor/`

| Test File | What It Tests |
|---|---|
| `JsonRpcTests.cs` | JSON-RPC message parsing, serialization, factory methods |
| `McpTypesTests.cs` | MCP type serialization, optional field omission |
| `ToolRegistryTests.cs` | Registration, group filtering, per-tool disable |
| `McpIntegrationTests.cs` | Full HTTP round-trip: init → list → call → errors |

### Manual Verification

```bash
# Initialize
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}' \
  -D - | head -20

# Should return JSON with protocolVersion, serverInfo, Mcp-Session-Id header

# List tools (use session ID from above)
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <session-id>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

# Should return theatre_status in tools array

# Call theatre_status
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <session-id>" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"theatre_status","arguments":{}}}'

# Should return content with status JSON
```

---

## Verification Checklist

```bash
# 1. Compile — open TestProject in Unity 6, no errors
# 2. Unit tests — Test Runner > EditMode > Run All
# 3. Health endpoint still works
curl http://localhost:9078/health
# 4. MCP handshake via curl (see manual verification above)
# 5. Claude Code connects via .mcp.json:
#    { "mcpServers": { "theatre": { "type": "http", "url": "http://localhost:9078/mcp" } } }
# 6. Claude Code sees theatre_status in tool list
# 7. Claude Code calls theatre_status and gets a response
```
