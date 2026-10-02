using System;
using System.Collections.Concurrent;

namespace WildCat
{
    /// <summary>
    /// Keeps posted actions in a thread-safe queue until <see cref="Pump"/> runs them, in order,
    /// on the thread that calls it. <c>WildCatBehaviour</c> pumps one from Update, which puts every
    /// event handler and Task completion of the SDK on Unity's main thread. Nothing runs while
    /// nobody pumps, so a client given a QueuedDispatcher needs someone to call Pump regularly.
    /// </summary>
    public sealed class QueuedDispatcher : IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        /// <summary>Actions waiting for the next <see cref="Pump"/>.</summary>
        public int Count
        {
            get { return queue.Count; }
        }

        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            queue.Enqueue(action);
        }

        /// <summary>
        /// Runs the waiting actions, at most <paramref name="maxActions"/> of them, and returns how
        /// many ran. Actions posted while pumping run in the same call unless the limit is reached.
        /// An action that throws is reported to <paramref name="onError"/> (or rethrown after the
        /// others ran when there is none).
        /// </summary>
        public int Pump(int maxActions = int.MaxValue, Action<Exception> onError = null)
        {
            int ran = 0;
            Exception first = null;
            while (ran < maxActions && queue.TryDequeue(out Action action))
            {
                ran++;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    if (onError != null)
                        onError(ex);
                    else if (first == null)
                        first = ex;
                }
            }
            if (first != null)
                throw new AggregateException("A WildCat callback threw.", first);
            return ran;
        }
    }
}
