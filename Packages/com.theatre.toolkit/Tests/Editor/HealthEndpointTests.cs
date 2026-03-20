using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Theatre.Editor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class HealthEndpointTests
    {
        [Test]
        public void ServerIsRunning()
        {
            Assert.IsTrue(TheatreServer.IsRunning,
                "TheatreServer should be running after editor initialization");
        }

        [Test]
        public async Task HealthEndpointReturnsOk()
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(TheatreServer.Url + "/health");

            Assert.AreEqual(200, (int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            StringAssert.Contains("\"status\":\"ok\"", body);
            StringAssert.Contains("\"port\":", body);
        }

        [Test]
        public async Task UnknownRouteReturns404()
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(TheatreServer.Url + "/nonexistent");

            Assert.AreEqual(404, (int)response.StatusCode);
        }
    }
}
