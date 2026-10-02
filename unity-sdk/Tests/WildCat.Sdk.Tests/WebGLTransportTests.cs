using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Unity;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// The browser transport's receive queue, compiled without UNITY_WEBGL (so without the .jslib
    /// calls). The browser delivers messages through the private Deliver, Closed and Fail methods,
    /// which the tests call by reflection the way the .jslib callbacks would.
    /// </summary>
    public class WebGLTransportTests
    {
        private static void Deliver(WebGLWebSocketTransport transport, byte[] message)
        {
            Invoke(transport, "Deliver", message);
        }

        private static void Invoke(WebGLWebSocketTransport transport, string method, params object[] args)
        {
            MethodInfo target = typeof(WebGLWebSocketTransport).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(target);
            target.Invoke(transport, args);
        }

        [Fact]
        public void WebGLReceiveDoesNotLeakTokenRegistrations()
        {
            // The connection passes the link's Stop token, which lives as long as the socket, to
            // every ReceiveAsync. Each receive must drop its registration on that token once it
            // completes, or every message received stays referenced until the socket closes.
            using var stop = new CancellationTokenSource();
            var transport = new WebGLWebSocketTransport(1024);
            List<WeakReference> receives = ReceiveMany(transport, stop.Token, 1000);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int alive = receives.Count(r => r.IsAlive);
            GC.KeepAlive(stop);
            Assert.True(alive == 0, alive + " of 1000 completed receives are still held by the token");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<WeakReference> ReceiveMany(WebGLWebSocketTransport transport, CancellationToken token, int count)
        {
            var result = new List<WeakReference>();
            for (int i = 0; i < count; i++)
            {
                Task<byte[]> receive = transport.ReceiveAsync(token);
                Deliver(transport, new byte[] { 1 });
                Assert.True(receive.IsCompletedSuccessfully);
                result.Add(new WeakReference(receive));
            }
            return result;
        }

        private static Task<byte[]> Receive(WebGLWebSocketTransport transport, CancellationToken token = default)
        {
            return transport.ReceiveAsync(token).WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task MessagesQueueUntilReceivedAndACancelledReceiveLosesNothing()
        {
            var transport = new WebGLWebSocketTransport(1024);
            Deliver(transport, new byte[] { 1 });
            Deliver(transport, new byte[] { 2 });
            Assert.Equal(new byte[] { 1 }, await Receive(transport));
            Assert.Equal(new byte[] { 2 }, await Receive(transport));

            using (var cancel = new CancellationTokenSource())
            {
                Task<byte[]> cancelled = transport.ReceiveAsync(cancel.Token);
                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            }
            // A message after a cancelled receive waits for the next one instead of being dropped.
            Deliver(transport, new byte[] { 3 });
            Assert.Equal(new byte[] { 3 }, await Receive(transport));

            Task<byte[]> waiting = Receive(transport);
            Invoke(transport, "Closed", true);
            Assert.Null(await waiting);
            Assert.Null(await Receive(transport));
        }

        [Fact]
        public async Task AnOversizedMessageFailsTheTransport()
        {
            var transport = new WebGLWebSocketTransport(4);
            Task<byte[]> waiting = Receive(transport);
            Deliver(transport, new byte[5]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => waiting);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Receive(transport));
        }
    }
}
