using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Runs server script events: C# the developer attached to a named event on the server,
    /// which runs for the logged in player. The trusted way to grant items, set scores and apply
    /// any rule a client must not decide on its own. The arguments are untrusted input to the
    /// script and must match the event's declared parameters in order and type.
    /// </summary>
    public sealed class ScriptsClient : FeatureClient
    {
        internal ScriptsClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>
        /// Runs <paramref name="eventName"/> with <paramref name="args"/>, each an int, short, long,
        /// bool, byte, byte[], float, double or string (anything else throws ArgumentException).
        /// Fails with <see cref="WildCatException"/> when the event does not exist, does not compile,
        /// throws or runs past its time limit.
        /// </summary>
        public Task<ScriptResult> ExecuteAsync(string eventName, params object[] args)
        {
            return ExecuteAsync(eventName, args, CancellationToken.None);
        }

        /// <summary>As <see cref="ExecuteAsync(string, object[])"/>, with a cancellation token.</summary>
        public async Task<ScriptResult> ExecuteAsync(string eventName, object[] args, CancellationToken cancellationToken)
        {
            RequireText(eventName, "eventName");
            ScriptEventExecutionRequestMessage request = new ScriptEventExecutionRequestMessage { eventName = eventName };
            request.SetParametersAndTypes(args ?? new object[0]);
            ScriptEventExecutionResponseMessage answer = await Request<ScriptEventExecutionResponseMessage>(
                WildCatMsgType.ExecuteScriptEventRequest, request, WildCatMsgType.ExecuteScriptEventResponse, cancellationToken).ConfigureAwait(false);
            return new ScriptResult(answer.hasReturnValue, answer.returnType, answer.returnValue);
        }

        /// <summary>Runs the event and returns its value as <typeparamref name="T"/>.</summary>
        public async Task<T> ExecuteAsync<T>(string eventName, params object[] args)
        {
            ScriptResult result = await ExecuteAsync(eventName, args, CancellationToken.None).ConfigureAwait(false);
            return result.GetValue<T>();
        }
    }
}
