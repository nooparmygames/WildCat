using Orleans.Runtime;

namespace WildCat.Grains
{
    /// <summary>
    /// Reproduces the Orleans 3.x default, where call chain reentrancy was on for every call
    /// (SchedulingOptions.AllowCallChainReentrancy = true). The legacy grains rely on it: for
    /// example Player.RejoinMatch calls RelayServerGame.PlayerRejoined, which calls back into the
    /// same Player. Orleans 7 and later removed the global option and made call chain reentrancy
    /// opt-in per call through RequestContext.AllowCallChainReentrancy(), so without this filter
    /// such a cycle deadlocks until the 30 second response timeout.
    ///
    /// Each outgoing grain call runs inside a reentrant section: while the call is pending, the
    /// callee and anything further down the chain may call back into the calling grain.
    ///
    /// This is a stopgap for the Orleans 10 port. Later steps should replace it with targeted
    /// RequestContext.AllowCallChainReentrancy() uses at the known cycles and then remove it.
    /// </summary>
    public sealed class CallChainReentrancyFilter : IOutgoingGrainCallFilter
    {
        public async Task Invoke(IOutgoingGrainCallContext context)
        {
            using var _ = RequestContext.AllowCallChainReentrancy();
            await context.Invoke();
        }
    }
}
