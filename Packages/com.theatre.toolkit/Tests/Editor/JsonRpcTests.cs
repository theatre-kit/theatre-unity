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
            var id = JToken.Parse("1");
            var result = JToken.Parse(@"{""ok"":true}");
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
            var id = JToken.Parse("1");
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
