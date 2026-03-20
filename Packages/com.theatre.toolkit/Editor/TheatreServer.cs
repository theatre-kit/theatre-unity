using System;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
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

        // Cached on main thread at startup for background thread access
        private static int s_cachedPort;
        private static string s_cachedEnabledGroups;
        private static ToolGroup s_cachedEnabledGroupsEnum;

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

            // Cache config values on main thread for background thread access
            s_cachedPort = TheatreConfig.Port;
            s_cachedEnabledGroupsEnum = TheatreConfig.EnabledGroups;
            s_cachedEnabledGroups = s_cachedEnabledGroupsEnum.ToString();

            // Create components
            s_toolRegistry = new ToolRegistry();
            s_sseManager = new SseStreamManager();

            // Register built-in tools
            RegisterBuiltInTools(s_toolRegistry);

            // Create MCP router with main thread dispatch
            // Note: getEnabledGroups is called from background threads.
            // EnabledGroups uses SessionState (main-thread-only), so return
            // the cached value instead. DisabledTools is in-memory, thread-safe.
            s_mcpRouter = new McpRouter(
                s_toolRegistry,
                () => s_cachedEnabledGroupsEnum,
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
            s_cachedEnabledGroupsEnum = groups;
            s_cachedEnabledGroups = groups.ToString();
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
            SceneSnapshotTool.Register(registry);
            SceneHierarchyTool.Register(registry);
            SceneInspectTool.Register(registry);
        }

        // --- Route Handlers ---

        private static void HandleHealth(HttpListenerContext context)
        {
            // Runs on background thread — use cached values, not SessionState
            var json = $"{{\"status\":\"ok\",\"version\":\"{TheatreConfig.ServerVersion}\""
                + $",\"port\":{s_cachedPort}"
                + $",\"client_connected\":{(IsClientConnected ? "true" : "false")}"
                + $",\"enabled_groups\":\"{s_cachedEnabledGroups}\""
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
