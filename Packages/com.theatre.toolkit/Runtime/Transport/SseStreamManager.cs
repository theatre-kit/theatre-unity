using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

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
