using System;
using System.Collections.Generic;
using System.Net;

namespace Theatre.Transport
{
    /// <summary>
    /// Routes HTTP requests by method + path to handler functions.
    /// </summary>
    public sealed class RequestRouter
    {
        private readonly Dictionary<(string method, string path), Action<HttpListenerContext>> _routes = new();
        private Action<HttpListenerContext> _notFoundHandler;

        /// <summary>
        /// Register a route handler.
        /// </summary>
        /// <param name="method">HTTP method (uppercase): "GET", "POST", "DELETE"</param>
        /// <param name="path">Absolute path: "/health", "/mcp"</param>
        /// <param name="handler">Handler called on background thread</param>
        public void Map(string method, string path, Action<HttpListenerContext> handler)
        {
            _routes[(method.ToUpperInvariant(), path)] = handler;
        }

        /// <summary>
        /// Set handler for unmatched routes. Default returns 404.
        /// </summary>
        public void SetNotFoundHandler(Action<HttpListenerContext> handler)
        {
            _notFoundHandler = handler;
        }

        /// <summary>
        /// Dispatch an incoming request to the matching handler.
        /// Called from a background thread.
        /// </summary>
        public void Dispatch(HttpListenerContext context)
        {
            var method = context.Request.HttpMethod.ToUpperInvariant();
            var path = context.Request.Url.AbsolutePath;

            if (_routes.TryGetValue((method, path), out var handler))
            {
                handler(context);
            }
            else if (_notFoundHandler != null)
            {
                _notFoundHandler(context);
            }
            else
            {
                SendNotFound(context);
            }
        }

        private static void SendNotFound(HttpListenerContext context)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(
                "{\"error\":\"not_found\",\"message\":\"No handler for this endpoint\"}");
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
    }
}
