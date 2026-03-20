using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class HttpTransportTests
    {
        private HttpTransport _transport;
        private const int TestPort = 19078; // Avoid conflicting with real server

        [TearDown]
        public void TearDown()
        {
            _transport?.Dispose();
            _transport = null;
        }

        [Test]
        public void StartAndStopWithoutError()
        {
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            Assert.IsTrue(_transport.IsListening);

            _transport.Stop();
            Assert.IsFalse(_transport.IsListening);
        }

        [Test]
        public async Task HandlerReceivesRequests()
        {
            bool handlerCalled = false;
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", ctx =>
            {
                handlerCalled = true;
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            });

            using var client = new HttpClient();
            await client.GetAsync($"http://localhost:{TestPort}/");

            Assert.IsTrue(handlerCalled);
        }

        [Test]
        public void CanRestartAfterStop()
        {
            _transport = new HttpTransport();

            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            _transport.Stop();

            // Should not throw
            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            Assert.IsTrue(_transport.IsListening);
        }

        [Test]
        public async Task HandlerExceptionReturns500()
        {
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", _ =>
            {
                throw new InvalidOperationException("test error");
            });

            LogAssert.Expect(LogType.Exception,
                "InvalidOperationException: test error");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{TestPort}/");

            Assert.AreEqual(500, (int)response.StatusCode);
        }
    }
}
