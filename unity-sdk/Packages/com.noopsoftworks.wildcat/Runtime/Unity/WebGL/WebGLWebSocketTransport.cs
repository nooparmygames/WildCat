using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace WildCat.Unity
{
    /// <summary>
    /// <see cref="ITransport"/> over the browser's WebSocket API, for WebGL players, where
    /// System.Net.WebSockets does not work. The JavaScript half is WildCatWebSocket.jslib next to
    /// this file. Everything runs on the browser's single thread. Outside WebGL players every
    /// method throws PlatformNotSupportedException; WildCatBehaviour picks this transport only
    /// in WebGL builds.
    /// </summary>
    /// <remarks>Not verified in a browser yet; see the package documentation.</remarks>
    public sealed class WebGLWebSocketTransport : ITransport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private delegate void OpenCallback(int socket);
        private delegate void MessageCallback(int socket, IntPtr data, int length);
        private delegate void ErrorCallback(int socket);
        private delegate void CloseCallback(int socket, int code);

        [DllImport("__Internal")]
        private static extern void WildCatWebSocket_SetCallbacks(OpenCallback onOpen, MessageCallback onMessage,
            ErrorCallback onError, CloseCallback onClose);

        [DllImport("__Internal")]
        private static extern int WildCatWebSocket_Create(string url);

        [DllImport("__Internal")]
        private static extern int WildCatWebSocket_Send(int socket, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern void WildCatWebSocket_Close(int socket, int code);

        [DllImport("__Internal")]
        private static extern void WildCatWebSocket_Free(int socket);

        private static readonly Dictionary<int, WebGLWebSocketTransport> Sockets = new Dictionary<int, WebGLWebSocketTransport>();
        private static bool callbacksSet;
#endif

        private readonly object sync = new object();
        private readonly Queue<byte[]> received = new Queue<byte[]>();
        private readonly int maxMessageBytes;
        private TaskCompletionSource<bool> opening = null;
        private TaskCompletionSource<byte[]> receiving;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int socket = -1;
#endif
        private bool closed;
        private Exception failure;

        public WebGLWebSocketTransport(int maxMessageBytes)
        {
            this.maxMessageBytes = maxMessageBytes;
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!callbacksSet)
            {
                WildCatWebSocket_SetCallbacks(OnOpen, OnMessage, OnError, OnClose);
                callbacksSet = true;
            }
            opening = new TaskCompletionSource<bool>();
            socket = WildCatWebSocket_Create(uri.AbsoluteUri);
            if (socket < 0)
                return Task.FromException(new InvalidOperationException("The browser could not open a WebSocket to " + uri + "."));
            Sockets[socket] = this;
            TaskCompletionSource<bool> pending = opening;
            cancellationToken.Register(() =>
            {
                if (pending.TrySetCanceled())
                    Abort();
            });
            return pending.Task;
#else
            throw new PlatformNotSupportedException("WebGLWebSocketTransport works only in WebGL players.");
#endif
        }

        public Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (WildCatWebSocket_Send(socket, message, message.Length) == 0)
                return Task.FromException(new InvalidOperationException("The WebSocket is not open."));
            return Task.CompletedTask;
#else
            throw new PlatformNotSupportedException("WebGLWebSocketTransport works only in WebGL players.");
#endif
        }

        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<byte[]> waiting;
            lock (sync)
            {
                if (received.Count > 0)
                    return Task.FromResult(received.Dequeue());
                if (failure != null)
                    return Task.FromException<byte[]>(failure);
                if (closed)
                    return Task.FromResult<byte[]>(null);
                receiving = new TaskCompletionSource<byte[]>();
                waiting = receiving;
            }
            cancellationToken.Register(() => waiting.TrySetCanceled());
            return waiting.Task;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socket >= 0)
                WildCatWebSocket_Close(socket, 1000);
#endif
            return Task.CompletedTask;
        }

        public void Abort()
        {
            Fail(new InvalidOperationException("The WebSocket was aborted."));
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socket >= 0)
                WildCatWebSocket_Close(socket, 1000);
#endif
        }

        public void Dispose()
        {
            Fail(new ObjectDisposedException(nameof(WebGLWebSocketTransport)));
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socket >= 0)
            {
                Sockets.Remove(socket);
                WildCatWebSocket_Free(socket);
                socket = -1;
            }
#endif
        }

        private void Deliver(byte[] message)
        {
            if (message.Length > maxMessageBytes)
            {
                Fail(new InvalidOperationException("The server sent a message larger than " + maxMessageBytes + " bytes."));
                return;
            }
            TaskCompletionSource<byte[]> waiting;
            lock (sync)
            {
                if (closed)
                    return;
                if (receiving == null)
                {
                    received.Enqueue(message);
                    return;
                }
                waiting = receiving;
                receiving = null;
            }
            waiting.TrySetResult(message);
        }

        private void Closed(bool gracefully)
        {
            TaskCompletionSource<byte[]> waiting;
            lock (sync)
            {
                if (!gracefully && failure == null)
                    failure = new InvalidOperationException("The WebSocket closed unexpectedly.");
                closed = true;
                waiting = receiving;
                receiving = null;
            }
            if (opening != null)
                opening.TrySetException(failure ?? new InvalidOperationException("The WebSocket closed before it opened."));
            if (waiting == null)
                return;
            if (failure != null)
                waiting.TrySetException(failure);
            else
                waiting.TrySetResult(null);
        }

        private void Fail(Exception error)
        {
            TaskCompletionSource<byte[]> waiting;
            lock (sync)
            {
                if (failure == null)
                    failure = error;
                closed = true;
                waiting = receiving;
                receiving = null;
            }
            if (opening != null)
                opening.TrySetException(failure);
            if (waiting != null)
                waiting.TrySetException(failure);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [MonoPInvokeCallback(typeof(OpenCallback))]
        private static void OnOpen(int id)
        {
            WebGLWebSocketTransport transport;
            if (Sockets.TryGetValue(id, out transport) && transport.opening != null)
                transport.opening.TrySetResult(true);
        }

        [MonoPInvokeCallback(typeof(MessageCallback))]
        private static void OnMessage(int id, IntPtr data, int length)
        {
            WebGLWebSocketTransport transport;
            if (!Sockets.TryGetValue(id, out transport))
                return;
            byte[] message = new byte[length];
            Marshal.Copy(data, message, 0, length);
            transport.Deliver(message);
        }

        [MonoPInvokeCallback(typeof(ErrorCallback))]
        private static void OnError(int id)
        {
            WebGLWebSocketTransport transport;
            if (Sockets.TryGetValue(id, out transport))
                transport.Fail(new InvalidOperationException("The browser reported a WebSocket error."));
        }

        [MonoPInvokeCallback(typeof(CloseCallback))]
        private static void OnClose(int id, int code)
        {
            WebGLWebSocketTransport transport;
            if (Sockets.TryGetValue(id, out transport))
                transport.Closed(code == 1000);
        }
#endif
    }
}
