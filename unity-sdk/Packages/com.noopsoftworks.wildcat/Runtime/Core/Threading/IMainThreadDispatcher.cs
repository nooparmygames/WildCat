using System;

namespace WildCat
{
    /// <summary>
    /// Where the SDK runs application code. The network loops run on background threads; every
    /// event handler and the completion of every Task the public API returns go through
    /// <see cref="Post"/>. In Unity, <c>WildCatBehaviour</c> supplies a <see cref="QueuedDispatcher"/>
    /// and pumps it from Update, so handlers and continuations run on the main thread. Outside Unity
    /// the default is <see cref="InlineDispatcher"/>, which runs them on the network thread at once.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>
        /// Runs <paramref name="action"/> on the application's thread, in the order posted. Called
        /// from any thread; must not block and must not throw.
        /// </summary>
        void Post(Action action);
    }
}
