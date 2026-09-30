using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// ExecuteScriptEvent (66 -> 67): runs one of the game's script events for the logged in player
    /// (<see cref="IScriptEventRunner"/> keyed by the session's game and player, with the session of
    /// the request) and answers with the script's return value, or hasReturnValue false for an event
    /// that returns nothing or null.
    /// <para>
    /// Error code 1 with a text the client can show when the run fails for a reason of the script
    /// or the request: the event does not exist; the arguments do not fit (wrong count, or a value
    /// of the wrong type); the script ran past its time limit or call depth; the game has too many
    /// runs still going past their limit; the stored script is no longer valid or returned a type
    /// events may not return; the script API refused a call; or the script threw Exception,
    /// InvalidOperationException or ArgumentException, the only exceptions a script can create,
    /// whose messages the developer wrote for the game client. Anything else, such as a
    /// NullReferenceException in the script or a failure of the cluster, is an internal error
    /// (code -1). The old code sent every exception message with code -1.
    /// </para>
    /// </summary>
    internal sealed class ExecuteScriptEventHandler
        : RequestHandler<ScriptEventExecutionRequestMessage, ScriptEventExecutionResponseMessage>
    {
        private static readonly Regex ParameterSuffix = new Regex(@" \(Parameter '[^']*'\)$", RegexOptions.CultureInvariant);

        public override WildCatMsgType RequestType => WildCatMsgType.ExecuteScriptEventRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.ExecuteScriptEventResponse;

        protected override async Task<ScriptEventExecutionResponseMessage> HandleAsync(MessageContext context,
            ScriptEventExecutionRequestMessage request)
        {
            var runner = context.Grains.GetGrain<IScriptEventRunner>(context.Session.GameKey, context.Session.UserID, null);
            object result;
            try
            {
                result = await runner.Run(context.Grains.GetGrain<ISession>(context.Ticket), request.eventName, request.parameters);
            }
            catch (Exception ex) when (IsScriptFailure(ex))
            {
                throw new RequestFailedException(GatewayErrors.Refused, TextFor(ex));
            }

            var response = new ScriptEventExecutionResponseMessage { hasReturnValue = result != null };
            if (response.hasReturnValue)
            {
                response.returnType = TypeOf(result);
                response.returnValue = result;
            }
            return response;
        }

        /// <summary>
        /// Whether <paramref name="ex"/> is a failure of the script run whose message is written
        /// for the developer and the client (by the sandbox, the script API or the script itself)
        /// rather than a server fault.
        /// </summary>
        private static bool IsScriptFailure(Exception ex)
        {
            return ex is ScriptEventNotFoundException
                || ex is ScriptTimeoutException
                || ex is ScriptThrottledException
                || ex is ScriptRecursionLimitException
                || ex is InvalidOperationException
                || ex is ArgumentException
                || ex is UnauthorizedAccessException
                || ex is InvalidCastException
                || ex.GetType() == typeof(Exception);
        }

        /// <summary>
        /// The exception's message, without the " (Parameter 'name')" .NET appends to an
        /// ArgumentException's (matched as text, since it may have crossed a grain call).
        /// </summary>
        private static string TextFor(Exception ex)
        {
            return ex is ArgumentException ? ParameterSuffix.Replace(ex.Message, "") : ex.Message;
        }

        /// <summary>
        /// The wire type of a return value. The sandbox only lets these types through
        /// (ScriptCompilationUtility.IsAllowedReturnValue), so anything else is a server bug.
        /// </summary>
        private static ParamType TypeOf(object value)
        {
            switch (value)
            {
                case bool _: return ParamType.BoolParam;
                case byte _: return ParamType.ByteParam;
                case short _: return ParamType.ShortParam;
                case int _: return ParamType.IntParam;
                case long _: return ParamType.LongParam;
                case float _: return ParamType.FloatParam;
                case double _: return ParamType.DoubleParam;
                case string _: return ParamType.StringParam;
                case byte[] _: return ParamType.ByteArrayParam;
                default:
                    throw new InvalidOperationException($"A script event returned a {value.GetType()}, which has no wire type.");
            }
        }
    }
}
