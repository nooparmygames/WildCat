using System;

namespace WildCat.Gateway
{
    /// <summary>
    /// Thrown by a handler to answer its request with a failed OptionalResponse carrying
    /// <see cref="ErrorCode"/> and the message, which the client sees. Use it for expected
    /// outcomes (wrong password, already exists); the dispatcher logs it at Information without a
    /// stack trace.
    /// </summary>
    public sealed class RequestFailedException : Exception
    {
        public RequestFailedException(int errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; }
    }
}
