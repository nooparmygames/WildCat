# Streams, broadcast channels and observers

All (docs) unless marked. Three mechanisms exist for pushing events out of a grain.

| Need | Use |
|---|---|
| Fan-out to all grains of a type, delivery may be lost, no subscription bookkeeping | Broadcast channel |
| Named streams with explicit or implicit subscriptions, clients can subscribe, same API as durable queues | Streams (memory provider in dev, Azure Queue / Event Hubs / SQS / Kinesis in prod) |
| Notify a specific connected client object, best effort | Observer (`IGrainObserver`) |
| Return a sequence to one caller | `IAsyncEnumerable<T>` grain method |

The legacy backend used `AddSimpleMessageStreamProvider("SMS")` with named streams
(`ChallengeEvents`, `FriendEvents`) and client-side subscriptions from the WebSocket gateway.
SMS was removed in 7.0. Memory streams are the drop-in replacement for that usage (explicit
subscriptions, client subscribers); broadcast channels replace SMS only for implicit,
grain-only subscribers.

## Streams

Configuration (silo and, for client subscribers, the client too):

```csharp
silo.AddMemoryStreams("game", o => o.ConfigurePartitioning(partitionCount: 8));   // do not change partition count after deploy
silo.AddMemoryGrainStorage("PubSubStore");   // subscriptions are persisted by PubSubRendezvousGrain in this store
client.AddMemoryStreams("game");
```

Packages: `Microsoft.Orleans.Streaming` (memory provider included), `.Streaming.AzureStorage`
(`AddAzureQueueStreams`), `.Streaming.EventHubs` (rewindable), `.Streaming.SQS`. Give
`PubSubStore` a durable provider in production when subscriptions must survive restarts.

API:

```csharp
var provider = this.GetStreamProvider("game");                 // client: clusterClient.GetStreamProvider("game")
var stream = provider.GetStream<FriendEvent>(StreamId.Create("FriendEvents", playerId));
await stream.OnNextAsync(evt);                                   // produce
var handle = await stream.SubscribeAsync((item, token) => Handle(item));   // consume; returns StreamSubscriptionHandle<T>
await handle.UnsubscribeAsync();
```

- Stream identity is `StreamId.Create(namespace, key)`; key can be string, Guid or long. Ids are
  strings underneath (7.0 change).
- `IAsyncStream<T>` implements `IAsyncObserver<T>` (OnNextAsync, OnCompletedAsync, OnErrorAsync)
  and `IAsyncObservable<T>` (SubscribeAsync).
- A subscription belongs to the grain, not the activation, and lives until unsubscribed.
  Subscribing twice delivers twice. On reactivation an explicitly subscribed grain must call
  `stream.GetAllSubscriptionHandles()` and `handle.ResumeAsync(observer)` on each (or
  `UnsubscribeAsync` the stale ones), typically in `OnActivateAsync`. Doing `SubscribeAsync`
  again instead creates a duplicate subscription.
- Implicit subscriptions: `[ImplicitStreamSubscription("FriendEvents")]` on the grain class maps
  stream `<ns, key>` to grain `<key>` of that class and activates it on first event. The grain
  still attaches its handler, either in `OnActivateAsync` via `SubscribeAsync`, or by
  implementing `IStreamSubscriptionObserver` and calling `handleFactory.Create<T>()` then
  `handle.ResumeAsync(this)` in `OnSubscribed`. One implicit subscription per namespace, no
  unsubscribe, no multiplicity.
- Ordering and delivery depend on the provider. Memory streams and broadcast channels are
  best effort and not rewindable; Azure Queue is at-least-once without FIFO under failure;
  Event Hubs is rewindable through `StreamSequenceToken`.
- Clients and grains use the same API, which is what makes a WebSocket gateway able to
  subscribe on behalf of a connected player and unsubscribe on disconnect.
- Stateless worker grains must not subscribe.

## Broadcast channels

```csharp
silo.AddBroadcastChannel("game-events", o => o.FireAndForgetDelivery = false);

[ImplicitChannelSubscription]
public sealed class LobbyGrain : Grain, ILobbyGrain, IOnBroadcastChannelSubscribed
{
    public Task OnSubscribed(IBroadcastChannelSubscription sub) =>
        sub.Attach<LobbyEvent>(OnEvent, ex => Task.CompletedTask);
    private Task OnEvent(LobbyEvent e) => Task.CompletedTask;
}

// publishing, from a grain, a hosted service or a client:
var provider = clusterClient.GetBroadcastChannelProvider("game-events");
var writer = provider.GetChannelWriter<LobbyEvent>(ChannelId.Create("game-events", Guid.Empty));
await writer.Publish(evt);
```

Not persistent, no history, only implicit subscriptions, messages to inactive grains are lost.
Package `Microsoft.Orleans.BroadcastChannel`. Same behavior as the old SMS provider for the
implicit case.

## Observers

```csharp
public interface IPlayerConnectionObserver : IGrainObserver
{
    [OneWay] Task Deliver(byte[] frame);         // Task/ValueTask only; [OneWay] avoids waiting for the client
}

// grain side
private readonly ObserverManager<IPlayerConnectionObserver> _subs = new(TimeSpan.FromMinutes(5), logger);
public Task Subscribe(IPlayerConnectionObserver o) { _subs.Subscribe(o, o); return Task.CompletedTask; }
public Task Unsubscribe(IPlayerConnectionObserver o) { _subs.Unsubscribe(o); return Task.CompletedTask; }
private Task Push(byte[] frame) => _subs.NotifyAsync(o => o.Deliver(frame));

// client (gateway) side
var obj = grainFactory.CreateObjectReference<IPlayerConnectionObserver>(connectionHandler);
await playerGrain.Subscribe(obj);
// ... on disconnect:
await playerGrain.Unsubscribe(obj);
grainFactory.DeleteObjectReference<IPlayerConnectionObserver>(obj);
```

- Observers are unreliable by design: a client crash loses them and a restarted client gets new
  identities, so `ObserverManager` expires entries and clients must resubscribe on a timer
  shorter than the expiry.
- The client keeps only a weak reference to the observer object; hold your own reference, and
  always `DeleteObjectReference` or the client leaks.
- Observer methods run one at a time per reference (no interleaving, attributes ignored).
- Since 9.0 observer methods accept `CancellationToken`.
- Grains can be observers too (`this.AsReference<IMyObserver>()`, no object reference needed).

## Picking one for the gateway

For a WebSocket gateway co-hosted with the silo, the straightforward design is: one connection
handler per socket, an `IPlayerConnectionObserver` registered on the player's session grain with
periodic resubscription, and `[OneWay]` delivery for relayed game frames. Use a memory stream
per game or per player when several grains need to publish to the same client without knowing
about each other. Whichever is chosen, treat lost pushes as possible and let the client
resynchronize by request.
