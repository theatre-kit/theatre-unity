using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Main Theatre server. Starts on editor load via [InitializeOnLoad].
    /// Owns the HTTP transport and request routing.
    /// </summary>
    [InitializeOnLoad]
    public static class TheatreServer
    {
        private static HttpTransport s_transport;
        private static RequestRouter s_router;

        /// <summary>Whether the server is currently running.</summary>
        public static bool IsRunning => s_transport?.IsListening ?? false;

        /// <summary>The URL the server is listening on.</summary>
        public static string Url => IsRunning
            ? $"http://localhost:{TheatreConfig.Port}"
            : null;

        static TheatreServer()
        {
            // Delay start to avoid issues during early editor initialization
            EditorApplication.delayCall += StartServer;
            EditorApplication.quitting += StopServer;
        }

        /// <summary>
        /// Start or restart the HTTP server.
        /// </summary>
        public static void StartServer()
        {
            StopServer();

            s_router = new RequestRouter();
            RegisterRoutes(s_router);

            s_transport = new HttpTransport();
            try
            {
                s_transport.Start(TheatreConfig.HttpPrefix, s_router.Dispatch);
                Debug.Log($"[Theatre] Server started on {Url}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] Failed to start server: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the HTTP server and release the port.
        /// </summary>
        public static void StopServer()
        {
            if (s_transport != null)
            {
                s_transport.Stop();
                s_transport = null;
                s_router = null;
            }
        }

        private static void RegisterRoutes(RequestRouter router)
        {
            router.Map("GET", "/health", HandleHealth);
        }

        // --- Route Handlers ---

        private static void HandleHealth(HttpListenerContext context)
        {
            var response = new StringBuilder();
            response.Append('{');
            response.Append("\"status\":\"ok\"");
            response.Append(",\"version\":\"");
            response.Append("0.0.1");
            response.Append('"');
            response.Append(",\"port\":");
            response.Append(TheatreConfig.Port);
            response.Append(",\"play_mode\":");
            // EditorApplication.isPlaying must be read on main thread,
            // but for /health we just report false on background thread.
            // Phase 1 will marshal to main thread properly.
            response.Append("false");
            response.Append('}');

            var body = Encoding.UTF8.GetBytes(response.ToString());
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
    }
}
