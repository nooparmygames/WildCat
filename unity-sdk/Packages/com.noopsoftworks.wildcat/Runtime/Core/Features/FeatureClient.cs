using System;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Base of the feature clients of <see cref="WildCatClient"/>. Every call returns a Task that
    /// completes through the client's dispatcher (Unity's main thread in a game) and fails with
    /// <see cref="WildCatException"/> for a server error, TimeoutException when the answer does not
    /// come within the request timeout, and <see cref="WildCatDisconnectedException"/> when the
    /// connection is not open.
    /// </summary>
    public abstract class FeatureClient
    {
        internal FeatureClient(WildCatConnection connection)
        {
            Connection = connection;
        }

        internal WildCatConnection Connection { get; }

        /// <summary>A request with the session ticket in the frame header, the usual kind.</summary>
        internal Task<T> Request<T>(WildCatMsgType requestType, MessageBase payload, WildCatMsgType responseType,
            CancellationToken cancellationToken, TimeSpan? timeout = null)
            where T : MessageBase, new()
        {
            return Connection.SendRequestAsync<T>(requestType, payload, responseType, true, null, timeout, false, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="start"/> and turns an exception it throws (a bad argument, no
        /// session) into a faulted Task, so every failure reaches the caller the same way and
        /// <c>task.AsCoroutine()</c> never throws. Async methods get this from the compiler.
        /// </summary>
        internal static Task Guard(Func<Task> start)
        {
            try
            {
                return start();
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <summary>As <see cref="Guard(Func{Task})"/>, for calls with a result.</summary>
        internal static Task<T> Guard<T>(Func<Task<T>> start)
        {
            try
            {
                return start();
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        /// <summary>Raises a feature event; push handlers already run on the dispatcher's thread.</summary>
        internal void Raise<T>(Action<T> handler, T args)
        {
            Connection.InvokeSafely(handler, args);
        }

        internal static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(name + " must not be empty.", name);
        }

        /// <summary>Cloud data and publisher data keys are limited to 100 characters.</summary>
        internal static void RequireKey(string key)
        {
            RequireText(key, "key");
            if (key.Length > 100)
                throw new ArgumentException("Keys are limited to 100 characters.", "key");
        }
    }
}
