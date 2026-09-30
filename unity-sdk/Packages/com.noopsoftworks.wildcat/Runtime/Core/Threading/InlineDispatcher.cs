using System;

namespace WildCat
{
    /// <summary>
    /// Runs every posted action at once on the thread that posts it, which is a network thread.
    /// The default outside Unity (servers, tools, tests). Handlers then must be thread safe and
    /// quick: a handler that blocks holds up the connection's receive loop.
    /// </summary>
    public sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public static readonly InlineDispatcher Instance = new InlineDispatcher();

        public void Post(Action action)
        {
            action();
        }
    }
}
