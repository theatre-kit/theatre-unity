using System;
using System.Net;
using System.Text;
using System.Threading;

namespace Theatre.Transport
{
    /// <summary>
    /// Lightweight HTTP server wrapping System.Net.HttpListener.
    /// Runs the accept loop on a background thread.
    /// Request handlers execute on the calling thread (background) —
    /// callers must marshal to the main thread if Unity APIs are needed.
    /// </summary>
    public sealed class HttpTransport : IDisposable
    {
        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private Action<HttpListenerContext> _requestHandler;

        /// <summary>
        /// Whether the server is currently listening.
        /// </summary>
        public bool IsListening => _running && (_listener?.IsListening ?? false);

        /// <summary>
        /// The prefix the server is bound to (e.g., "http://localhost:9078/").
        /// </summary>
        public string Prefix { get; private set; }

        /// <summary>
        /// Start listening on the given prefix.
        /// </summary>
        /// <param name="prefix">HTTP prefix, e.g., "http://localhost:9078/"</param>
        /// <param name="requestHandler">
        /// Called on a thread pool thread for each incoming request.
        /// The handler is responsible for writing the response and closing it.
        /// </param>
        public void Start(string prefix, Action<HttpListenerContext> requestHandler)
        {
            if (_running) Stop();

            Prefix = prefix;
            _requestHandler = requestHandler;

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _running = true;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "Theatre.HttpAccept"
            };
            _acceptThread.Start();
        }

        /// <summary>
        /// Stop the server and release the port.
        /// </summary>
        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* swallow on shutdown */ }
            try { _listener?.Close(); } catch { /* swallow on shutdown */ }
            _listener = null;
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    // GetContext blocks until a request arrives or listener stops
                    var context = _listener.GetContext();
                    // Handle each request on a thread pool thread
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException) when (!_running)
                {
                    // Expected when Stop() is called — listener.GetContext throws
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                _requestHandler?.Invoke(context);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                try
                {
                    context.Response.StatusCode = 500;
                    var body = Encoding.UTF8.GetBytes(
                        "{\"error\":\"internal_server_error\"}");
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = body.Length;
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
                catch { /* best effort */ }
            }
        }
    }
}
