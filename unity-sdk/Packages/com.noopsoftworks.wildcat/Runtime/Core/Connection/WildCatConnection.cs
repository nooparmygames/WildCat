using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// The WebSocket connection to the gateway: opens and closes the socket, sends frames from a
    /// queue with a single writer, reads frames on a receive loop, matches answers to requests by
    /// request id (with a timeout per request), hands server pushes to the registered handlers,
    /// sends heartbeats and measures the round trip time, and reconnects with exponential backoff
    /// when the socket drops.
    /// </summary>
    /// <remarks>
    /// With the default scheduler, public members can be called from any thread and socket work
    /// can run on background threads. A PumpedConnectionScheduler keeps the work on its pumping
    /// thread instead; create and use that client on the same thread. Event handlers, push handlers
    /// and the completion of the Tasks returned
    /// by <see cref="RequestAsync{TResponse}"/> go through the <see cref="IMainThreadDispatcher"/>
    /// (Unity's main thread in a game). Most code uses the feature clients of
    /// <see cref="WildCatClient"/> rather than this class.
    /// </remarks>
    public sealed class WildCatConnection : IDisposable
    {
        /// <summary>How long a close handshake may take before the socket is dropped.</summary>
        private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(3);

        /// <summary>Queued after the last frame to make the writer send the close message.</summary>
        private static readonly byte[] CloseMarker = new byte[0];

        private readonly WildCatClientOptions options;
        private readonly ITransportFactory transportFactory;
        private readonly IMainThreadDispatcher dispatcher;
        private readonly IWildCatLogger logger;
        private readonly IConnectionScheduler scheduler;
        private readonly object gate = new object();
        private readonly ConcurrentDictionary<uint, PendingRequest> pending = new ConcurrentDictionary<uint, PendingRequest>();
        private readonly Random random = new Random();
        private readonly object roundTripLock = new object();

        private Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]> handlers = new Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]>();
        private ConnectionState state = ConnectionState.Disconnected;
        private DisconnectReason lastDisconnectReason = DisconnectReason.NotConnected;
        private Link link;
        private CancellationTokenSource lifetime;
        private bool closing;
        private bool disposed;
        private int lastRequestId;
        private string ticket;
        private bool loggedIn;
        private TimeSpan roundTripTime;
        private TimeSpan smoothedRoundTripTime;

        /// <param name="options">Server URL, timeouts and reconnection settings; copied.</param>
        /// <param name="dispatcher">Where handlers and completions run; null runs them inline on the network thread.</param>
        /// <param name="transportFactory">Creates the socket for each attempt; null uses ClientWebSocket.</param>
        /// <param name="logger">Receives log messages; null drops them.</param>
        /// <param name="scheduler">Deadlines and delays; null uses managed timers. Web players must pass a PumpedConnectionScheduler and pump it every frame.</param>
        public WildCatConnection(WildCatClientOptions options, IMainThreadDispatcher dispatcher = null,
            ITransportFactory transportFactory = null, IWildCatLogger logger = null, IConnectionScheduler scheduler = null)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            this.options = options.Clone();
            this.options.Validate();
            this.dispatcher = dispatcher ?? InlineDispatcher.Instance;
            this.transportFactory = transportFactory ?? new ClientWebSocketTransportFactory();
            this.logger = logger ?? NullWildCatLogger.Instance;
            this.scheduler = scheduler ?? SystemConnectionScheduler.Instance;
        }

        /// <summary>Raised when ConnectAsync has opened the socket.</summary>
        public event Action Connected;

        /// <summary>
        /// Raised once when the connection is closed for good: DisconnectAsync, a drop with
        /// reconnection turned off, reconnection giving up, or the server refusing to resume the
        /// session. Not raised for a drop that is followed by reconnection attempts.
        /// </summary>
        public event Action<DisconnectedEventArgs> Disconnected;

        /// <summary>Raised when the socket dropped and before each reconnection attempt.</summary>
        public event Action<ReconnectingEventArgs> Reconnecting;

        /// <summary>
        /// Raised when a reconnection succeeded and the session was resumed (initialized again
        /// and, if a player was logged in, logged in again). Matches are not rejoined on their own:
        /// check Matches.GetPendingAsync and call Matches.RejoinAsync.
        /// </summary>
        public event Action Reconnected;

        /// <summary>Every frame the server pushed (events, relayed game messages), after the registered handlers ran.</summary>
        public event Action<WildCatMessageBase> MessageReceived;

        /// <summary>The current state.</summary>
        public ConnectionState State
        {
            get
            {
                lock (gate)
                    return state;
            }
        }

        /// <summary>True while the connection is open and not reconnecting.</summary>
        public bool IsConnected
        {
            get { return State == ConnectionState.Connected; }
        }

        /// <summary>The URL this connection opens.</summary>
        public Uri ServerUri
        {
            get { return options.ServerUri; }
        }

        /// <summary>The settings in use (a copy of the ones given).</summary>
        internal WildCatClientOptions Options
        {
            get { return options; }
        }

        internal IWildCatLogger Logger
        {
            get { return logger; }
        }

        internal bool CaptureContext => scheduler.CaptureContext;

        /// <summary>
        /// The round trip time of the last answered heartbeat, from the moment its frame went to the
        /// socket (the wait behind other queued frames does not count), or zero before the first one.
        /// </summary>
        public TimeSpan RoundTripTime
        {
            get
            {
                lock (roundTripLock)
                    return roundTripTime;
            }
        }

        /// <summary>The round trip time averaged over recent heartbeats (7/8 old, 1/8 new), or zero before the first one.</summary>
        public TimeSpan SmoothedRoundTripTime
        {
            get
            {
                lock (roundTripLock)
                    return smoothedRoundTripTime;
            }
        }

        /// <summary>Called on a network thread after a reconnection, to initialize the session and log in again.</summary>
        internal Func<CancellationToken, Task> ResumeHandler { get; set; }

        /// <summary>Called on the thread that closed the connection for good, before <see cref="Disconnected"/> is raised.</summary>
        internal Action ClosedHandler { get; set; }

        /// <summary>The session ticket sent in frame headers, or null before a session is initialized.</summary>
        internal string Ticket
        {
            get
            {
                lock (gate)
                    return ticket;
            }
        }

        internal bool IsLoggedIn
        {
            get
            {
                lock (gate)
                    return loggedIn;
            }
        }

        /// <summary>True once DisconnectAsync or Dispose started closing the connection.</summary>
        private bool IsClosing
        {
            get
            {
                lock (gate)
                    return closing || disposed;
            }
        }

        internal void SetSession(string newTicket, bool isLoggedIn)
        {
            lock (gate)
            {
                ticket = newTicket;
                loggedIn = isLoggedIn;
            }
        }

        /// <summary>
        /// Opens the socket. Throws <see cref="WildCatDisconnectedException"/> when it cannot and
        /// InvalidOperationException when the connection is not disconnected. Like every Task of
        /// the SDK, it completes through the dispatcher.
        /// </summary>
        public Task ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return CompleteThroughDispatcher(ConnectCoreAsync(cancellationToken));
        }

        private async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource connectionLifetime;
            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(WildCatConnection));
                if (state != ConnectionState.Disconnected)
                    throw new InvalidOperationException("ConnectAsync was called while the connection is " + state + ".");
                state = ConnectionState.Connecting;
                closing = false;
                lifetime = new CancellationTokenSource();
                connectionLifetime = lifetime;
            }

            Link fresh;
            try
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionLifetime.Token))
                    fresh = await OpenLinkAsync(linked.Token).ConfigureAwait(CaptureContext);
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (state == ConnectionState.Connecting)
                        state = ConnectionState.Disconnected;
                }
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Connecting was cancelled.", ex, cancellationToken);
                Log(WildCatLogLevel.Warning, "Could not connect to " + options.ServerUri + ": " + ex.Message, null);
                throw new WildCatDisconnectedException("Could not connect to " + options.ServerUri + ": " + ex.Message,
                    DisconnectReason.ConnectFailed, ex);
            }

            lock (gate)
            {
                if (closing || disposed || state != ConnectionState.Connecting)
                {
                    fresh.Abort(DisconnectReason.ClientClosed, null);
                    fresh.Transport.Dispose();
                    throw new WildCatDisconnectedException("The connection was closed while it was being opened.", DisconnectReason.ClientClosed);
                }
                link = fresh;
                state = ConnectionState.Connected;
            }
            Log(WildCatLogLevel.Info, "Connected to " + options.ServerUri, null);
            Raise(Connected);
            StartLoops(fresh);
        }

        /// <summary>
        /// Closes the connection: sends what is queued, closes the socket with a handshake (or
        /// drops it after a few seconds), stops reconnecting and fails the requests still waiting.
        /// Raises <see cref="Disconnected"/> once. ConnectAsync can open it again afterwards.
        /// Completes through the dispatcher unless there was nothing to close.
        /// </summary>
        public Task DisconnectAsync()
        {
            return CompleteThroughDispatcher(DisconnectCoreAsync());
        }

        private async Task DisconnectCoreAsync()
        {
            Link current;
            CancellationTokenSource connectionLifetime;
            lock (gate)
            {
                if (state == ConnectionState.Disconnected)
                    return;
                closing = true;
                current = link;
                connectionLifetime = lifetime;
            }
            CancelQuietly(connectionLifetime);

            if (current != null)
            {
                current.RequestClose();
                Task first = await Task.WhenAny(current.Receiver, scheduler.DelayAsync(CloseHandshakeTimeout)).ConfigureAwait(CaptureContext);
                if (first != current.Receiver)
                {
                    current.Abort(DisconnectReason.ClientClosed, null);
                    await Task.WhenAny(current.Receiver, scheduler.DelayAsync(CloseHandshakeTimeout)).ConfigureAwait(CaptureContext);
                }
                // The receive loop normally reported the close already; this covers a loop that did not end.
                OnLinkLost(current, DisconnectReason.ClientClosed, null);
            }
            CloseForGood(DisconnectReason.ClientClosed, null);
        }

        /// <summary>
        /// Sends a request and returns the payload of its answer. Fails with
        /// <see cref="WildCatException"/> when the server answers with an error (including the
        /// gateway's ErrorMsg refusals), TimeoutException when no answer comes in time, and
        /// <see cref="WildCatDisconnectedException"/> when the connection is not open or drops
        /// first. The Task completes through the dispatcher.
        /// </summary>
        /// <typeparam name="TResponse">The payload class inside the answer's OptionalResponse.</typeparam>
        public Task<TResponse> RequestAsync<TResponse>(WildCatMsgType requestType, MessageBase payload, WildCatMsgType responseType,
            RequestOptions requestOptions = null, CancellationToken cancellationToken = default(CancellationToken))
            where TResponse : MessageBase, new()
        {
            RequestOptions settings = requestOptions ?? new RequestOptions();
            return SendRequestAsync<TResponse>(requestType, payload, responseType, settings.SendTicket, settings.GameTarget,
                settings.Timeout, false, cancellationToken);
        }

        /// <summary>
        /// Queues a frame exactly as given (no request id or ticket is added). Returns false when
        /// the connection is not open or the send queue is full (see
        /// <see cref="WildCatClientOptions.MaxQueuedMessages"/>). Throws ArgumentException for a
        /// frame larger than <see cref="WildCatClientOptions.MaxMessageBytes"/>, which the gateway
        /// would answer by closing the socket. Relay.Send is the usual way to send game messages.
        /// </summary>
        public bool Send(WildCatMessageBase frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            byte[] encoded = WireCodec.Encode(frame);
            if (encoded.Length > options.MaxMessageBytes)
                throw TooLarge(frame.msgType, encoded.Length);
            Link current;
            lock (gate)
            {
                if (state != ConnectionState.Connected)
                    return false;
                current = link;
            }
            return current != null && current.TrySend(encoded, null, false) == SendResult.Queued;
        }

        private ArgumentException TooLarge(WildCatMsgType type, int size)
        {
            return new ArgumentException("The " + type + " message is " + size + " bytes, more than WildCatClientOptions.MaxMessageBytes ("
                                         + options.MaxMessageBytes + "); the gateway would close the connection. Nothing was sent.");
        }

        /// <summary>
        /// Calls <paramref name="handler"/> through the dispatcher for every pushed frame of
        /// <paramref name="type"/> (answers to requests never reach handlers). Dispose the result to
        /// remove it. Handlers may be added and removed from any thread.
        /// </summary>
        public IDisposable RegisterHandler(WildCatMsgType type, Action<WildCatMessageBase> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            lock (gate)
            {
                Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]> copy =
                    new Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]>(handlers);
                Action<WildCatMessageBase>[] existing;
                copy.TryGetValue(type, out existing);
                Action<WildCatMessageBase>[] updated = new Action<WildCatMessageBase>[existing == null ? 1 : existing.Length + 1];
                if (existing != null)
                    Array.Copy(existing, updated, existing.Length);
                updated[updated.Length - 1] = handler;
                copy[type] = updated;
                handlers = copy;
            }
            return new HandlerRegistration(this, type, handler);
        }

        private void RemoveHandler(WildCatMsgType type, Action<WildCatMessageBase> handler)
        {
            lock (gate)
            {
                Action<WildCatMessageBase>[] existing;
                if (!handlers.TryGetValue(type, out existing))
                    return;
                int index = Array.IndexOf(existing, handler);
                if (index < 0)
                    return;
                Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]> copy =
                    new Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]>(handlers);
                if (existing.Length == 1)
                {
                    copy.Remove(type);
                }
                else
                {
                    Action<WildCatMessageBase>[] updated = new Action<WildCatMessageBase>[existing.Length - 1];
                    Array.Copy(existing, 0, updated, 0, index);
                    Array.Copy(existing, index + 1, updated, index, existing.Length - index - 1);
                    copy[type] = updated;
                }
                handlers = copy;
            }
        }

        /// <summary>
        /// Drops the connection at once without raising events (for OnDestroy and application
        /// quit). Waiting requests fail with <see cref="WildCatDisconnectedException"/>.
        /// </summary>
        public void Dispose()
        {
            Link current;
            CancellationTokenSource connectionLifetime;
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                closing = true;
                current = link;
                connectionLifetime = lifetime;
                link = null;
                state = ConnectionState.Disconnected;
                lastDisconnectReason = DisconnectReason.ClientClosed;
                ticket = null;
                loggedIn = false;
            }
            CancelQuietly(connectionLifetime);
            if (current != null)
                current.Abort(DisconnectReason.ClientClosed, null);
            foreach (uint id in pending.Keys)
                Complete(id, r => r.Fail(new WildCatDisconnectedException("The client was disposed.", DisconnectReason.ClientClosed)), false);
        }

        /// <summary>
        /// The request machinery behind every call. <paramref name="isInternal"/> requests
        /// (heartbeats, session resume) may run while reconnecting, skip the send queue limits and
        /// complete on the network thread instead of through the dispatcher.
        /// </summary>
        /// <remarks>
        /// <c>onAnswer</c> runs on the network thread when a successful answer arrives, before the
        /// completion is posted to the dispatcher, and only while the socket that carried the
        /// request is still the current one and the connection is not closing. It runs under the
        /// connection's lock, so it must be quick and must not call back into the connection's
        /// public API. Session state goes there, so that an answer the game pumps late cannot
        /// overwrite what a reconnection or a disconnect did in between. When the socket is gone or
        /// the connection closed, the answer is ignored and the request fails with
        /// <see cref="WildCatDisconnectedException"/>; when the hook throws, the request fails
        /// with that exception. Its second argument is the round trip: from the moment the writer
        /// handed the frame to the socket until the answer arrived, so time spent waiting in the
        /// send queue behind other frames does not count as network delay.
        /// </remarks>
        internal Task<T> SendRequestAsync<T>(WildCatMsgType requestType, MessageBase payload, WildCatMsgType responseType,
            bool sendTicket, string gameTarget, TimeSpan? timeout, bool isInternal, CancellationToken cancellationToken,
            Action<T, TimeSpan> onAnswer = null)
            where T : MessageBase, new()
        {
            if (payload == null)
                return Task.FromException<T>(new ArgumentNullException(nameof(payload)));
            Link current;
            string currentTicket;
            lock (gate)
            {
                current = link;
                bool usable = !disposed && current != null && current.Alive
                              && (state == ConnectionState.Connected || (isInternal && state == ConnectionState.Reconnecting));
                if (!usable)
                    return Task.FromException<T>(NotConnected(requestType));
                currentTicket = ticket;
            }
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);
            if ((sendTicket || gameTarget != null) && string.IsNullOrEmpty(currentTicket))
            {
                return Task.FromException<T>(new InvalidOperationException(
                    requestType + " needs a session. Call Session.InitializeAsync (and log in) first."));
            }

            uint id = unchecked((uint)Interlocked.Increment(ref lastRequestId));
            byte[] encoded;
            try
            {
                WildCatMessageBase frame;
                if (gameTarget != null)
                {
                    frame = WildCatMessageBase.CreateWithGameTarget(payload, requestType, WildCatMsgHeader.normal, gameTarget, currentTicket);
                    frame.SetRequestID(id);
                }
                else if (sendTicket)
                {
                    frame = WildCatMessageBase.CreateWithAuthDataAndRequestID(payload, requestType, WildCatMsgHeader.normal, currentTicket, id);
                }
                else
                {
                    frame = WildCatMessageBase.CreateWithRequestID(payload, requestType, WildCatMsgHeader.normal, id);
                }
                encoded = WireCodec.Encode(frame);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
            if (encoded.Length > options.MaxMessageBytes)
                return Task.FromException<T>(TooLarge(requestType, encoded.Length));

            PendingRequest<T> request = new PendingRequest<T>(id, requestType, responseType, current, isInternal, onAnswer);
            pending[id] = request;

            TimeSpan limit = timeout ?? options.RequestTimeout;
            if (limit > TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan)
            {
                request.TimeoutRegistration = scheduler.Schedule(limit, () => Complete(id,
                    r => r.Fail(new TimeoutException(requestType + " got no answer within " + limit.TotalSeconds + " seconds.")), true));
            }
            if (cancellationToken.CanBeCanceled)
                request.CancelRegistration = cancellationToken.Register(() => Complete(id, r => r.Cancel(cancellationToken), false));

            // A link that died between the check above and here has already failed the requests it
            // knew about, so check again after queueing: either this sees it dead, or the link's
            // cleanup runs later and fails this request too.
            SendResult queued = current.TrySend(encoded, request, isInternal);
            if (queued == SendResult.Full)
            {
                Complete(id, r => r.Fail(new WildCatException(WildCatErrorCodes.SendQueueFull,
                    "Cannot send " + requestType + ": the send queue is full (the connection is not keeping up). Nothing was sent.",
                    requestType)), false);
            }
            else if (queued != SendResult.Queued || !current.Alive)
            {
                Complete(id, r => r.Fail(NotConnected(requestType)), false);
            }
            return request.Completion.Task;
        }

        /// <summary>
        /// Runs a request's <c>onAnswer</c> hook if the answer still counts: the connection is not
        /// closing or disposed and the request's socket is the current one. Returns why the answer
        /// was not used, or null.
        /// </summary>
        private Exception ApplyAnswer(PendingRequest request, Action apply)
        {
            Exception refused = null;
            lock (gate)
            {
                if (disposed || closing)
                {
                    refused = new WildCatDisconnectedException(request.RequestType
                        + " was answered after the connection was closed; the answer was ignored.", DisconnectReason.ClientClosed);
                }
                else if (link != request.Link || !request.Link.Alive)
                {
                    refused = new WildCatDisconnectedException(request.RequestType
                        + " was answered on a socket that has since dropped; the answer was ignored.", DisconnectReason.ConnectionLost);
                }
                else
                {
                    try
                    {
                        apply();
                    }
                    catch (Exception ex)
                    {
                        refused = ex;
                    }
                }
            }
            if (refused != null && !(refused is WildCatDisconnectedException))
                Log(WildCatLogLevel.Warning, "Applying the answer to " + request.RequestType + " failed: " + refused.Message, null);
            return refused;
        }

        private WildCatDisconnectedException NotConnected(WildCatMsgType requestType)
        {
            ConnectionState now;
            DisconnectReason reason;
            lock (gate)
            {
                now = disposed ? ConnectionState.Disconnected : state;
                reason = now == ConnectionState.Reconnecting ? DisconnectReason.Reconnecting
                    : now == ConnectionState.Disconnected ? lastDisconnectReason : DisconnectReason.NotConnected;
            }
            string what = now == ConnectionState.Reconnecting
                ? "the connection is reconnecting; wait for Reconnected"
                : "the connection is " + now.ToString().ToLowerInvariant();
            return new WildCatDisconnectedException("Cannot send " + requestType + ": " + what + ".", reason);
        }

        /// <summary>
        /// Makes <paramref name="work"/>'s outcome arrive through the dispatcher, so connecting and
        /// disconnecting complete on the same thread as requests and events. Work that finished
        /// at once is returned as it is.
        /// </summary>
        private Task CompleteThroughDispatcher(Task work)
        {
            if (work.IsCompleted)
                return work;
            return AwaitThenYieldToDispatcherAsync(work);
        }

        private async Task AwaitThenYieldToDispatcherAsync(Task work)
        {
            try
            {
                await work.ConfigureAwait(CaptureContext);
            }
            catch (Exception)
            {
                await YieldToDispatcher().ConfigureAwait(CaptureContext);
                throw;
            }
            await YieldToDispatcher().ConfigureAwait(CaptureContext);
        }

        private Task YieldToDispatcher()
        {
            TaskCompletionSource<bool> reached = new TaskCompletionSource<bool>();
            Post(() => reached.TrySetResult(true));
            return reached.Task;
        }

        private void Complete(uint id, Func<PendingRequest, Action> outcome, bool fromTimeout)
        {
            PendingRequest request;
            if (!pending.TryRemove(id, out request))
                return;
            request.MarkFinished();
            request.ReleaseTimers(fromTimeout);
            Action complete = outcome(request);
            if (request.IsInternal)
                complete();
            else
                Post(complete);
        }

        private async Task<Link> OpenLinkAsync(CancellationToken cancellationToken)
        {
            ITransport transport = transportFactory.Create(options);
            try
            {
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    using var deadline = scheduler.Schedule(options.ConnectTimeout, () => CancelQuietly(limit));
                    try
                    {
                        await transport.ConnectAsync(options.ServerUri, limit.Token).ConfigureAwait(CaptureContext);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Opening the socket took longer than " + options.ConnectTimeout.TotalSeconds + " seconds.");
                    }
                }
                return new Link(transport, options.MaxQueuedMessages, options.MaxQueuedBytes, CaptureContext);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        private void StartLoops(Link fresh)
        {
            fresh.Writer = WriteLoopAsync(fresh);
            fresh.Receiver = ReceiveLoopAsync(fresh);
            if (options.HeartbeatInterval > TimeSpan.Zero)
                fresh.Heartbeat = HeartbeatLoopAsync(fresh);
        }

        private async Task ReceiveLoopAsync(Link current)
        {
            DisconnectReason reason = DisconnectReason.ServerClosed;
            Exception error = null;
            try
            {
                while (true)
                {
                    byte[] message = await current.Transport.ReceiveAsync(current.Stop.Token).ConfigureAwait(CaptureContext);
                    if (message == null)
                        break;
                    HandleFrame(message);
                }
                // The server closed the socket. Answer its close message through the writer (the
                // only one allowed to send) and give it a moment to do so.
                current.RequestClose();
                await Task.WhenAny(current.Writer, scheduler.DelayAsync(CloseHandshakeTimeout)).ConfigureAwait(CaptureContext);
            }
            catch (Exception ex)
            {
                reason = DisconnectReason.ConnectionLost;
                error = ex;
            }
            if (current.WasAborted)
            {
                reason = current.StopReason;
                error = current.StopError ?? error;
            }
            OnLinkLost(current, reason, error);
        }

        private async Task WriteLoopAsync(Link current)
        {
            try
            {
                while (true)
                {
                    await current.WaitForFrameAsync().ConfigureAwait(CaptureContext);
                    Outgoing next;
                    if (!current.TryDequeue(out next))
                        continue;
                    if (ReferenceEquals(next.Bytes, CloseMarker))
                    {
                        using (CancellationTokenSource limit = new CancellationTokenSource())
                        using (scheduler.Schedule(CloseHandshakeTimeout, () => CancelQuietly(limit)))
                            await current.Transport.CloseAsync(limit.Token).ConfigureAwait(CaptureContext);
                        return;
                    }
                    // A request that timed out or was cancelled while it waited in the queue is not
                    // sent: nobody waits for its answer, and the caller, told it failed, should be
                    // able to count on the server never having run it.
                    if (next.Request != null)
                    {
                        if (next.Request.IsFinished)
                            continue;
                        next.Request.MarkSent();
                    }
                    await current.Transport.SendAsync(next.Bytes, current.Stop.Token).ConfigureAwait(CaptureContext);
                }
            }
            catch (OperationCanceledException) when (current.Stop.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (current.Alive)
                {
                    Log(WildCatLogLevel.Debug, "Sending failed: " + ex.Message, null);
                    current.Abort(DisconnectReason.ConnectionLost, ex);
                }
            }
        }

        private async Task HeartbeatLoopAsync(Link current)
        {
            while (current.Alive)
            {
                try
                {
                    await scheduler.DelayAsync(options.HeartbeatInterval, current.Stop.Token).ConfigureAwait(CaptureContext);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool withLogin;
                lock (gate)
                {
                    // While resuming, the resume requests prove the socket works; the ticket is not ready.
                    if (link != current || state != ConnectionState.Connected)
                        continue;
                    withLogin = loggedIn;
                }

                // Measured from the moment the frame went out, not from when it was queued: a queue
                // full of relay frames is the game's own backlog, not network delay.
                TimeSpan measured = TimeSpan.Zero;
                try
                {
                    // HeartBeat keeps the player online on the server but needs a login; before that,
                    // GetServerTime (no session needed) keeps the socket busy and measures the time.
                    if (withLogin)
                    {
                        await SendRequestAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(),
                            WildCatMsgType.HeartBeatResponse, true, null, options.HeartbeatTimeout, true, CancellationToken.None,
                            (answer, roundTrip) => measured = roundTrip).ConfigureAwait(CaptureContext);
                    }
                    else
                    {
                        await SendRequestAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest, new EmptyMessage(),
                            WildCatMsgType.GetServerTimeResponse, false, null, options.HeartbeatTimeout, true, CancellationToken.None,
                            (answer, roundTrip) => measured = roundTrip).ConfigureAwait(CaptureContext);
                    }
                    RecordRoundTrip(measured);
                }
                catch (WildCatException)
                {
                    // Any answer shows the socket works, even a refusal (an expired login, for
                    // example). A refusal carries no round trip, so the last one stays.
                }
                catch (TimeoutException ex)
                {
                    if (current.Alive)
                    {
                        Log(WildCatLogLevel.Warning, "The server did not answer a heartbeat within "
                                                     + options.HeartbeatTimeout.TotalSeconds + " seconds; dropping the socket.", null);
                        current.Abort(DisconnectReason.HeartbeatTimeout, ex);
                    }
                    return;
                }
                catch (Exception)
                {
                    if (!current.Alive)
                        return;
                }
            }
        }

        private void RecordRoundTrip(TimeSpan elapsed)
        {
            lock (roundTripLock)
            {
                roundTripTime = elapsed;
                smoothedRoundTripTime = smoothedRoundTripTime == TimeSpan.Zero
                    ? elapsed
                    : TimeSpan.FromTicks((smoothedRoundTripTime.Ticks * 7 + elapsed.Ticks) / 8);
            }
        }

        private void HandleFrame(byte[] message)
        {
            WildCatMessageBase frame;
            try
            {
                frame = WireCodec.Decode(message);
            }
            catch (Exception ex)
            {
                Log(WildCatLogLevel.Warning, "Dropped a message from the server that could not be read: " + ex.Message, null);
                return;
            }

            bool isInternalType = frame.msgType <= WildCatMsgType.HighestInternal;
            if (isInternalType && frame.hasRequestID)
            {
                PendingRequest request;
                if (pending.TryRemove(frame.requestID, out request))
                {
                    request.MarkFinished();
                    request.ReleaseTimers(false);
                    // Decodes the answer and applies its session state here, on the network
                    // thread, before the completion waits in the dispatcher.
                    Action complete = request.Answer(frame, this);
                    if (request.IsInternal)
                        complete();
                    else
                        Post(complete);
                    return;
                }
                if (frame.msgType != WildCatMsgType.ErrorMsg)
                {
                    // An answer that came after its request timed out or was cancelled.
                    Log(WildCatLogLevel.Debug, "Ignored a late " + frame.msgType + " for request " + frame.requestID + ".", null);
                    return;
                }
            }
            if (frame.msgType == WildCatMsgType.ErrorMsg)
            {
                string text = "";
                try
                {
                    text = frame.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg;
                }
                catch (Exception)
                {
                }
                Log(WildCatLogLevel.Warning, "The server refused a message: " + text, null);
                return;
            }
            DispatchPush(frame);
        }

        private void DispatchPush(WildCatMessageBase frame)
        {
            Dictionary<WildCatMsgType, Action<WildCatMessageBase>[]> snapshot;
            lock (gate)
                snapshot = handlers;
            Action<WildCatMessageBase>[] list;
            snapshot.TryGetValue(frame.msgType, out list);
            Action<WildCatMessageBase> any = MessageReceived;
            if (list == null && any == null)
            {
                Log(WildCatLogLevel.Debug, "No handler for a pushed " + frame.msgType + ".", null);
                return;
            }
            Post(() =>
            {
                if (list != null)
                {
                    foreach (Action<WildCatMessageBase> handler in list)
                        InvokeSafely(handler, frame);
                }
                if (any != null)
                    InvokeSafely(any, frame);
            });
        }

        private void OnLinkLost(Link current, DisconnectReason reason, Exception error)
        {
            if (!current.MarkDead())
                return;
            CancelQuietly(current.Stop);
            try
            {
                current.Transport.Dispose();
            }
            catch (Exception)
            {
            }

            foreach (KeyValuePair<uint, PendingRequest> entry in pending)
            {
                if (entry.Value.Link != current)
                    continue;
                WildCatMsgType type = entry.Value.RequestType;
                DisconnectReason why = reason;
                Exception cause = error;
                Complete(entry.Key, r => r.Fail(new WildCatDisconnectedException(
                    type + " got no answer: the connection closed (" + why + ").", why, cause)), false);
            }

            bool reconnect = false;
            CancellationToken stop = CancellationToken.None;
            lock (gate)
            {
                if (link != current)
                    return;
                link = null;
                if (closing || disposed)
                {
                    reason = DisconnectReason.ClientClosed;
                    error = null;
                }
                else if (state == ConnectionState.Reconnecting)
                {
                    // The reconnection loop owns this link and sees its resume fail.
                    return;
                }
                else if (state == ConnectionState.Connected && options.AutoReconnect)
                {
                    state = ConnectionState.Reconnecting;
                    loggedIn = false;
                    reconnect = true;
                    stop = lifetime.Token;
                }
            }

            if (reconnect)
            {
                Log(WildCatLogLevel.Warning, "The connection dropped (" + reason + (error == null ? "" : ": " + error.Message) + "); reconnecting.", null);
                Task ignored = ReconnectLoopAsync(reason, error, stop);
                return;
            }
            CloseForGood(reason, error);
        }

        private async Task ReconnectLoopAsync(DisconnectReason dropReason, Exception dropError, CancellationToken stop)
        {
            Exception lastError = dropError;
            int attempt = 0;
            while (true)
            {
                attempt++;
                if (options.MaxReconnectAttempts > 0 && attempt > options.MaxReconnectAttempts)
                {
                    Log(WildCatLogLevel.Warning, "Gave up reconnecting after " + options.MaxReconnectAttempts + " attempts.", null);
                    CloseForGood(DisconnectReason.ReconnectFailed, lastError);
                    return;
                }

                TimeSpan delay = options.GetReconnectDelay(attempt, NextRandom());
                Raise(Reconnecting, new ReconnectingEventArgs(attempt, delay, dropReason, lastError));
                try
                {
                    await scheduler.DelayAsync(delay, stop).ConfigureAwait(CaptureContext);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Link fresh;
                try
                {
                    fresh = await OpenLinkAsync(stop).ConfigureAwait(CaptureContext);
                }
                catch (Exception ex)
                {
                    if (stop.IsCancellationRequested)
                        return;
                    lastError = ex;
                    Log(WildCatLogLevel.Info, "Reconnection attempt " + attempt + " failed: " + ex.Message, null);
                    continue;
                }

                lock (gate)
                {
                    if (stop.IsCancellationRequested || state != ConnectionState.Reconnecting)
                    {
                        fresh.Abort(DisconnectReason.ClientClosed, null);
                        fresh.Transport.Dispose();
                        return;
                    }
                    link = fresh;
                    ticket = null;
                    loggedIn = false;
                }
                StartLoops(fresh);

                try
                {
                    Func<CancellationToken, Task> resume = ResumeHandler;
                    if (resume != null)
                        await resume(stop).ConfigureAwait(CaptureContext);
                }
                catch (WildCatException ex) when (ex.ErrorCode == WildCatErrorCodes.Refused || ex.ErrorCode == WildCatErrorCodes.AlreadyExists)
                {
                    // The server is there but refuses the old session or login (the game key is
                    // gone, the password changed); trying again will not change that. Other codes,
                    // such as -1 (an internal error while a silo restarts, for example), are
                    // retried below with the usual backoff.
                    Log(WildCatLogLevel.Error, "Reconnected, but the server refused to resume the session: " + ex.Message, null);
                    CloseForGood(DisconnectReason.ResumeFailed, ex);
                    fresh.Abort(DisconnectReason.ClientClosed, null);
                    return;
                }
                catch (Exception ex)
                {
                    // DisconnectAsync or Dispose ended the connection while it resumed; they close it.
                    if (stop.IsCancellationRequested || IsClosing)
                        return;
                    lastError = ex;
                    Log(ex is WildCatException ? WildCatLogLevel.Warning : WildCatLogLevel.Info,
                        "Resuming the session after reconnection attempt " + attempt + " failed: " + ex.Message, null);
                    fresh.Abort(DisconnectReason.ConnectionLost, ex);
                    continue;
                }

                bool resumed;
                lock (gate)
                {
                    if (stop.IsCancellationRequested)
                        return;
                    resumed = link == fresh && fresh.Alive && state == ConnectionState.Reconnecting;
                    if (resumed)
                        state = ConnectionState.Connected;
                }
                if (resumed)
                {
                    Log(WildCatLogLevel.Info, "Reconnected to " + options.ServerUri + " after " + attempt + " attempt(s).", null);
                    Raise(Reconnected);
                    return;
                }
                lastError = new WildCatDisconnectedException("The connection dropped while the session was resumed.", DisconnectReason.ConnectionLost);
            }
        }

        /// <summary>Moves to Disconnected and raises <see cref="Disconnected"/> if the connection was open.</summary>
        private void CloseForGood(DisconnectReason reason, Exception error)
        {
            bool wasOpen;
            bool raise;
            lock (gate)
            {
                wasOpen = state == ConnectionState.Connected || state == ConnectionState.Reconnecting;
                state = ConnectionState.Disconnected;
                link = null;
                ticket = null;
                loggedIn = false;
                if (wasOpen)
                    lastDisconnectReason = reason;
                raise = wasOpen && !disposed;
            }
            if (!raise)
                return;
            Log(reason == DisconnectReason.ClientClosed ? WildCatLogLevel.Info : WildCatLogLevel.Warning,
                "Disconnected (" + reason + (error == null ? "" : ": " + error.Message) + ").", null);
            Action closed = ClosedHandler;
            if (closed != null)
            {
                try
                {
                    closed();
                }
                catch (Exception ex)
                {
                    Log(WildCatLogLevel.Error, "Clearing the session state failed.", ex);
                }
            }
            Raise(Disconnected, new DisconnectedEventArgs(reason, error));
        }

        private double NextRandom()
        {
            lock (random)
                return random.NextDouble();
        }

        internal void Post(Action action)
        {
            try
            {
                dispatcher.Post(action);
            }
            catch (Exception ex)
            {
                Log(WildCatLogLevel.Error, "The dispatcher refused a callback.", ex);
            }
        }

        private void Raise(Action handler)
        {
            if (handler == null)
                return;
            Post(() =>
            {
                foreach (Delegate single in handler.GetInvocationList())
                {
                    try
                    {
                        ((Action)single)();
                    }
                    catch (Exception ex)
                    {
                        Log(WildCatLogLevel.Error, "A WildCat event handler threw.", ex);
                    }
                }
            });
        }

        private void Raise<T>(Action<T> handler, T args)
        {
            if (handler == null)
                return;
            Post(() => InvokeSafely(handler, args));
        }

        /// <summary>Calls every handler of a multicast delegate, logging instead of stopping at one that throws.</summary>
        internal void InvokeSafely<T>(Action<T> handler, T args)
        {
            if (handler == null)
                return;
            foreach (Delegate single in handler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)single)(args);
                }
                catch (Exception ex)
                {
                    Log(WildCatLogLevel.Error, "A WildCat handler threw.", ex);
                }
            }
        }

        internal void Log(WildCatLogLevel level, string message, Exception exception)
        {
            try
            {
                logger.Log(level, message, exception);
            }
            catch (Exception)
            {
            }
        }

        private static void CancelQuietly(CancellationTokenSource source)
        {
            if (source == null)
                return;
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException)
            {
            }
        }

        private enum SendResult
        {
            Queued,
            Full,
            Closed,
        }

        /// <summary>A frame waiting for the writer, with the request it carries (null for relayed frames).</summary>
        private struct Outgoing
        {
            public Outgoing(byte[] bytes, PendingRequest request)
            {
                Bytes = bytes;
                Request = request;
            }

            public byte[] Bytes { get; }

            public PendingRequest Request { get; }
        }

        /// <summary>One socket and its loops. A reconnection makes a new one.</summary>
        private sealed class Link
        {
            private readonly object queueLock = new object();
            private readonly Queue<Outgoing> outbound = new Queue<Outgoing>();
            private readonly AsyncSignal signal;
            private readonly int maxQueuedMessages;
            private readonly long maxQueuedBytes;
            private long queuedBytes;
            private int alive = 1;
            private int closeRequested;
            private int aborted;

            public Link(ITransport transport, int maxQueuedMessages, long maxQueuedBytes, bool captureContext)
            {
                signal = new AsyncSignal(captureContext);
                Transport = transport;
                this.maxQueuedMessages = maxQueuedMessages;
                this.maxQueuedBytes = maxQueuedBytes;
            }

            public ITransport Transport { get; }

            public CancellationTokenSource Stop { get; } = new CancellationTokenSource();

            public Task Receiver { get; set; } = Task.CompletedTask;

            public Task Writer { get; set; } = Task.CompletedTask;

            public Task Heartbeat { get; set; } = Task.CompletedTask;

            public bool Alive
            {
                get { return Volatile.Read(ref alive) == 1; }
            }

            public bool WasAborted
            {
                get { return Volatile.Read(ref aborted) == 1; }
            }

            public DisconnectReason StopReason { get; private set; } = DisconnectReason.ConnectionLost;

            public Exception StopError { get; private set; }

            /// <summary>True for the one caller that marks the link dead.</summary>
            public bool MarkDead()
            {
                return Interlocked.Exchange(ref alive, 0) == 1;
            }

            /// <summary>
            /// Queues a frame for the writer. Full when the queue already holds its limit of frames
            /// or bytes (heartbeats and the resume pass <paramref name="ignoreLimits"/>, so a busy
            /// queue cannot hide a dead socket); Closed when the link is dead or closing.
            /// </summary>
            public SendResult TrySend(byte[] frame, PendingRequest request, bool ignoreLimits)
            {
                if (!Alive || Volatile.Read(ref closeRequested) == 1)
                    return SendResult.Closed;
                lock (queueLock)
                {
                    if (!ignoreLimits && (outbound.Count >= maxQueuedMessages || queuedBytes + frame.Length > maxQueuedBytes))
                        return SendResult.Full;
                    outbound.Enqueue(new Outgoing(frame, request));
                    queuedBytes += frame.Length;
                }
                signal.Release();
                return SendResult.Queued;
            }

            /// <summary>Makes the writer send the close message after the frames queued so far.</summary>
            public void RequestClose()
            {
                if (Interlocked.Exchange(ref closeRequested, 1) == 1)
                    return;
                lock (queueLock)
                    outbound.Enqueue(new Outgoing(CloseMarker, null));
                signal.Release();
            }

            public Task WaitForFrameAsync()
            {
                return signal.WaitAsync(Stop.Token);
            }

            public bool TryDequeue(out Outgoing frame)
            {
                lock (queueLock)
                {
                    if (outbound.Count == 0)
                    {
                        frame = default(Outgoing);
                        return false;
                    }
                    frame = outbound.Dequeue();
                    queuedBytes -= frame.Bytes.Length;
                    return true;
                }
            }

            /// <summary>Drops the socket; the receive loop then reports <paramref name="reason"/>.</summary>
            public void Abort(DisconnectReason reason, Exception error)
            {
                if (Interlocked.Exchange(ref aborted, 1) == 1)
                    return;
                StopReason = reason;
                StopError = error;
                CancelQuietly(Stop);
                try
                {
                    Transport.Abort();
                }
                catch (Exception)
                {
                }
            }
        }

        private abstract class PendingRequest
        {
            private int finished;
            private long sentAt;

            protected PendingRequest(uint id, WildCatMsgType requestType, WildCatMsgType responseType, Link link, bool isInternal)
            {
                Id = id;
                RequestType = requestType;
                ResponseType = responseType;
                Link = link;
                IsInternal = isInternal;
                sentAt = Stopwatch.GetTimestamp();
            }

            public uint Id { get; }

            /// <summary>True once the request left the pending table (answered, timed out, cancelled or failed).</summary>
            public bool IsFinished
            {
                get { return Volatile.Read(ref finished) == 1; }
            }

            public void MarkFinished()
            {
                Volatile.Write(ref finished, 1);
            }

            /// <summary>Called by the writer just before the frame goes to the socket; the round trip starts here.</summary>
            public void MarkSent()
            {
                Interlocked.Exchange(ref sentAt, Stopwatch.GetTimestamp());
            }

            /// <summary>The time since the frame went to the socket (since the request was made, until then).</summary>
            protected TimeSpan SinceSent()
            {
                long elapsed = Stopwatch.GetTimestamp() - Interlocked.Read(ref sentAt);
                return TimeSpan.FromTicks((long)(elapsed * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
            }

            public WildCatMsgType RequestType { get; }

            public WildCatMsgType ResponseType { get; }

            public Link Link { get; }

            public bool IsInternal { get; }

            public IDisposable TimeoutRegistration { get; set; }

            public CancellationTokenRegistration CancelRegistration { get; set; }

            /// <summary>
            /// Reads the answer and applies its hook (on the network thread) and returns what
            /// completes the Task.
            /// </summary>
            public abstract Action Answer(WildCatMessageBase frame, WildCatConnection owner);

            public abstract Action Fail(Exception error);

            public abstract Action Cancel(CancellationToken token);

            public void ReleaseTimers(bool fromTimeout)
            {
                TimeoutRegistration?.Dispose();
                CancelRegistration.Dispose();
            }
        }

        private sealed class PendingRequest<T> : PendingRequest where T : MessageBase, new()
        {
            private readonly Action<T, TimeSpan> onAnswer;

            public PendingRequest(uint id, WildCatMsgType requestType, WildCatMsgType responseType, Link link, bool isInternal,
                Action<T, TimeSpan> onAnswer)
                : base(id, requestType, responseType, link, isInternal)
            {
                this.onAnswer = onAnswer;
            }

            public TaskCompletionSource<T> Completion { get; } = new TaskCompletionSource<T>();

            public override Action Answer(WildCatMessageBase frame, WildCatConnection owner)
            {
                TimeSpan roundTrip = SinceSent();
                Exception error = null;
                T value = null;
                try
                {
                    if (frame.msgType == WildCatMsgType.ErrorMsg)
                    {
                        OptionalResponse<EmptyMessage> refusal = frame.GetPayload<OptionalResponse<EmptyMessage>>();
                        error = new WildCatException(refusal.errorCode, refusal.errorMsg, RequestType) { IsGatewayRefusal = true };
                    }
                    else if (frame.msgType != ResponseType)
                    {
                        error = new WildCatException(WildCatErrorCodes.UnexpectedResponse,
                            "Expected " + ResponseType + " but the server answered with " + frame.msgType + ".", RequestType);
                    }
                    else
                    {
                        OptionalResponse<T> response = frame.GetPayload<OptionalResponse<T>>();
                        if (response.isSuccesful)
                            value = response.msg ?? new T();
                        else
                            error = new WildCatException(response.errorCode, response.errorMsg, RequestType);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                if (error == null && onAnswer != null)
                    error = owner.ApplyAnswer(this, () => onAnswer(value, roundTrip));
                if (error != null)
                    return () => Completion.TrySetException(error);
                return () => Completion.TrySetResult(value);
            }

            public override Action Fail(Exception error)
            {
                return () => Completion.TrySetException(error);
            }

            public override Action Cancel(CancellationToken token)
            {
                return () => Completion.TrySetCanceled(token);
            }
        }

        private sealed class HandlerRegistration : IDisposable
        {
            private WildCatConnection owner;
            private readonly WildCatMsgType type;
            private readonly Action<WildCatMessageBase> handler;

            public HandlerRegistration(WildCatConnection owner, WildCatMsgType type, Action<WildCatMessageBase> handler)
            {
                this.owner = owner;
                this.type = type;
                this.handler = handler;
            }

            public void Dispose()
            {
                WildCatConnection current = Interlocked.Exchange(ref owner, null);
                if (current != null)
                    current.RemoveHandler(type, handler);
            }
        }
    }
}
