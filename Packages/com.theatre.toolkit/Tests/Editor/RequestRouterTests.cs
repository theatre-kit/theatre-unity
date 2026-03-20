using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class RequestRouterTests
    {
        // RequestRouter.Dispatch needs an HttpListenerContext which is hard
        // to construct in tests. Test the route registration logic:

        [Test]
        public void MapDoesNotThrow()
        {
            var router = new RequestRouter();
            Assert.DoesNotThrow(() =>
                router.Map("GET", "/health", _ => { }));
        }

        [Test]
        public void MapMultipleRoutesDoesNotThrow()
        {
            var router = new RequestRouter();
            router.Map("GET", "/health", _ => { });
            router.Map("POST", "/mcp", _ => { });
            router.Map("GET", "/mcp", _ => { });
        }
    }
}
