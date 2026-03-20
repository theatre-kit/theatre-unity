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
            var schema = JToken.Parse(@"{
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
            // isError should be omitted when false (default)
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

            var json = JsonConvert.SerializeObject(result);
            Assert.That(json, Does.Contain("\"isError\":true"));
        }
    }
}
