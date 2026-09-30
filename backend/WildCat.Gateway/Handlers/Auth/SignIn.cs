using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>Checks and texts the sign-in handlers share.</summary>
    internal static class SignIn
    {
        public const string LoginFailed = "The login failed.";
        public const string UnsupportedLoginType = "Unsupported login type";
        public const string WrongOrExpiredCode = "The code is wrong or has expired.";

        /// <summary>
        /// Checks the session ticket that Authenticate, CreateNewAccount and EmailActivation carry
        /// in their payload (older than the header ticket, and kept for the SDK). Touches the
        /// session and returns its key. A malformed ticket is error code 1; an unknown, ended or
        /// expired session throws the session exceptions, which the dispatcher answers with code 1.
        /// </summary>
        public static async Task<Guid> TouchPayloadTicketAsync(IGrainFactory grains, string ticket)
        {
            if (!Guid.TryParse(ticket, out Guid sessionKey))
                throw new RequestFailedException(GatewayErrors.Refused, GatewayErrors.InvalidTicket);
            await grains.GetGrain<ISession>(sessionKey).Touch();
            return sessionKey;
        }

        /// <summary>
        /// Email addresses are stored and mapped in lower case (Player.AddPlayerEmail), so lookups
        /// have to lower the address too. An address longer than
        /// <see cref="InputLimits.MaxEmailLength"/> is error code 1.
        /// </summary>
        public static string NormalizeEmail(string email)
        {
            return InputLimits.Check((email ?? "").Trim(), InputLimits.MaxEmailLength, "email address").ToLowerInvariant();
        }

        /// <summary>
        /// Checks the length of a login id: a device id or user name up to
        /// <see cref="InputLimits.MaxIdLength"/> characters (error code 1 otherwise). Email
        /// addresses are checked by <see cref="NormalizeEmail"/>.
        /// </summary>
        public static void CheckLoginId(WildCat.Protocol.LoginType type, string id)
        {
            if (type == WildCat.Protocol.LoginType.Device)
                InputLimits.Check(id, InputLimits.MaxIdLength, "device id");
            else if (type == WildCat.Protocol.LoginType.UserWithPassword)
                InputLimits.Check(id, InputLimits.MaxIdLength, "user name");
        }

        /// <summary>
        /// Runs a grain call whose ArgumentException means bad input (invalid characters, an
        /// unacceptable password, a wrong code) and turns it into error code 1 with its message.
        /// </summary>
        public static async Task<T> WithInputErrorsAsync<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (ArgumentException ex)
            {
                throw new RequestFailedException(GatewayErrors.Refused, MessageWithoutParameter(ex));
            }
        }

        /// <summary>
        /// The exception's message without the " (Parameter 'name')" .NET appends to it. Matched
        /// as text because an exception that crossed a grain call may carry the suffix in its
        /// message without the parameter name.
        /// </summary>
        private static string MessageWithoutParameter(ArgumentException ex)
        {
            return ParameterSuffix.Replace(ex.Message, "");
        }

        private static readonly Regex ParameterSuffix = new Regex(@" \(Parameter '[^']*'\)$", RegexOptions.CultureInvariant);
    }
}
