using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
            // Tool result is JSON-escaped inside the text field
            Assert.That(json, Does.Contain("\\\"status\\\":\\\"ok\\\""));
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
            var enumerator = response.Headers.GetValues("Mcp-Session-Id").GetEnumerator();
            enumerator.MoveNext();
            return enumerator.Current;
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
