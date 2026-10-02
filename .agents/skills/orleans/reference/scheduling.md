# Scheduling: single-threaded turns, reentrancy, timers, reminders, external tasks

All (docs).

## Execution model

An activation processes one request at a time from start to completion. Continuations after
`await` run on the activation's own `TaskScheduler`, so `await`, `Task.Delay`, `Task.WhenAll`,
`Task.WhenAny`, `ContinueWith` and `Task.Factory.StartNew` all stay in the grain context. While a
request awaits, the activation is idle but still busy, so other requests queue.

Deadlock: A awaits B while B awaits A (or any longer cycle, or a grain calling itself through a
reference). Nothing detects it; both calls time out after `ResponseTimeout` (30 s) with
`TimeoutException`.

## Reentrancy options

| Option | Scope | Effect |
|---|---|---|
| `[Reentrant]` | grain class | any request may interleave with any other |
| `[AlwaysInterleave]` | interface method | that method interleaves with everything, and everything with it |
| `[ReadOnly]` | interface method | interleaves with other `[ReadOnly]` calls only; declares no state mutation |
| `[MayInterleave(nameof(Predicate))]` | grain class | static `bool Predicate(IInvokable req)` decides per call |
| `using var _ = RequestContext.AllowCallChainReentrancy();` | call site | callees down this chain may call back into this grain until disposed |
| `RequestContext.SuppressCallChainReentrancy()` | call site | stops the above from flowing further |

Interleaving is still single-threaded: turns from different requests alternate at `await`
points, never in parallel. The classic bug is reading state, awaiting, then writing based on the
stale read; a counter under `[Reentrant]` undercounts. Prefer non-reentrant grains plus
`[ReadOnly]` for getters and `AllowCallChainReentrancy` for the one known cycle (for example a
session grain that calls a game grain that calls back into the session). The legacy relay
server grain in this repo was `[Reentrant]` to escape a deadlock with `RelayMessage`; the
targeted options above are the 7+ way to do that.

Transactional grains must be `[Reentrant]`.

## Timers

```csharp
private IGrainTimer? _timer;
public override Task OnActivateAsync(CancellationToken ct)
{
    _timer = this.RegisterGrainTimer(
        static (self, ct) => self.Tick(ct), this,
        new GrainTimerCreationOptions
        {
            DueTime = TimeSpan.FromSeconds(1),
            Period = TimeSpan.FromSeconds(10),     // measured from the end of the previous callback
            Interleave = false,                   // true restores the pre-8.2 RegisterTimer behavior
            KeepAlive = false                     // true stops idle collection while the timer runs
        });
    return Task.CompletedTask;
}
```

- Returns `IGrainTimer` with `Change(dueTime, period)`; dispose to cancel. The callback token is
  cancelled on dispose or deactivation. Callbacks go through grain call filters and tracing.
- Timers die with the activation and with the silo. They do not keep the grain alive unless
  `KeepAlive = true`.
- `Timeout.InfiniteTimeSpan` as period makes a one-shot timer.
- `RegisterTimer` is obsolete since 8.2. POCO grains use `ITimerRegistry.RegisterGrainTimer`.

## Reminders

```csharp
public sealed class TournamentGrain : Grain, ITournamentGrain, IRemindable
{
    public async Task Schedule() =>
        await this.RegisterOrUpdateReminder("close", dueTime: TimeSpan.FromHours(1), period: TimeSpan.FromHours(24));

    public async Task ReceiveReminder(string name, TickStatus status)
    {
        if (name == "close") { /* ... */ }
    }

    public async Task Cancel()
    {
        var r = await this.GetReminder("close");
        if (r is not null) await this.UnregisterReminder(r);
    }
}
```

- Durable: survive deactivation, restarts and silo loss; a tick activates the grain if needed.
  Missed ticks while the whole cluster is down are skipped, not replayed.
- Period should be minutes, hours or days; not a high-frequency timer.
- Need `Microsoft.Orleans.Reminders` plus a provider: `UseInMemoryReminderService()` (dev only),
  `UseAdoNetReminderService`, `UseRedisReminderService`, `UseAzureTableReminderService`,
  `UseCosmosReminderService`. Every silo in a heterogeneous cluster must configure reminders.
- `RegisterOrUpdateReminder`, `GetReminder`, `GetReminders`, `UnregisterReminder` are extension
  methods in 7+; call them as `this.X(...)`. `IGrainReminder` handles are not valid across
  activations; identify reminders by name.
- Combine: a reminder every few minutes that re-arms a short timer gives fine resolution that
  survives deactivation. The legacy `TimedExecuter` grain implemented exactly this by hand.

## External tasks and blocking

- `Task.Run(...)` and the `endMethod` of `Task.Factory.FromAsync` run on the thread pool, outside
  the grain. Code after `await Task.Run(...)` returns to the grain scheduler. Use it only for
  CPU-bound or blocking work that touches no grain state; grain calls from inside are allowed.
- `Task.Factory.StartNew(WorkerAsync).Unwrap()` runs an async worker under the grain scheduler.
  Without `Unwrap` you get a `Task<Task>` that completes immediately.
- `ConfigureAwait(false)` in grain code moves the continuation off the grain scheduler: never in
  grain code, fine inside libraries you call (the grain's own `await` brings you back).
- `.Wait()`, `.Result`, `Task.WaitAll/WaitAny`, `GetAwaiter().GetResult()` can deadlock the
  single-threaded activation. If sync-over-async is unavoidable, `await Task.Run(() => task.Wait())`.
- `async void`, including `List<T>.ForEach(async x => ...)` and any `Action<T>` coerced from an
  async lambda, crashes the process on an unhandled exception. Never in grains.
- Library calls that are truly async need no `Task.Run` offloading.

| Goal | Do |
|---|---|
| Background thread-pool work, no grain state | `Task.Run` |
| Async worker with grain guarantees | `Task.Factory.StartNew(WorkerAsync).Unwrap()` |
| Sync worker with grain guarantees | `Task.Factory.StartNew(WorkerSync)` |
| Timeout on work | `Task.Delay` + `Task.WhenAny` |
| Call an async library | plain `await` |
