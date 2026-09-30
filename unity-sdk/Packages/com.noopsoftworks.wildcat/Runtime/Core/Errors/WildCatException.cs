using System;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// The server answered a request with an error. <see cref="ErrorCode"/> is the code from the
    /// answer (see <see cref="WildCatErrorCodes"/>) and the exception message is the server's text.
    /// Codes from -1000 down are the SDK's own (an answer of the wrong type, a full send queue).
    /// </summary>
    public class WildCatException : Exception
    {
        public WildCatException(int errorCode, string message, WildCatMsgType requestType)
            : base(string.IsNullOrEmpty(message) ? "The server refused " + requestType + " with error code " + errorCode + "." : message)
        {
            ErrorCode = errorCode;
            RequestType = requestType;
        }

        /// <summary>The error code of the answer: 1 refused, 2 already exists, -1 internal error, a code the message documents, or one of the SDK's codes from -1000 down.</summary>
        public int ErrorCode { get; }

        /// <summary>The request the server refused.</summary>
        public WildCatMsgType RequestType { get; }

        /// <summary>
        /// True when the gateway refused the request before any handler ran (an ErrorMsg answer):
        /// no session ticket, an unknown or expired session, not logged in, or an unsupported message.
        /// </summary>
        public bool IsGatewayRefusal { get; internal set; }
    }
}
