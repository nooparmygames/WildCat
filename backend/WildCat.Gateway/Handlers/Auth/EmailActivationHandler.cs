using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// EmailActivation (80 -> 81): activates an email login with the code sent to it, on the
    /// session whose ticket is in the payload. A sign-in attempt. Error code 1 for an unknown
    /// address and for a wrong, used or expired code.
    /// </summary>
    internal sealed class EmailActivationHandler : RequestHandler<EmailActivationRequest, EmptyMessage>
    {
        public const string UnknownEmail = "No account has this email address.";

        public override WildCatMsgType RequestType => WildCatMsgType.EmailActivationRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.EmailActivationResponse;
        public override SessionRequirement Session => SessionRequirement.None;
        public override bool IsSignInAttempt => true;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, EmailActivationRequest request)
        {
            await SignIn.TouchPayloadTicketAsync(context.Grains, request.ticket);
            string email = SignIn.NormalizeEmail(request.email);
            string playerId = email.Length == 0 ? null
                : await context.Grains.GetGrain<IEmailToAccountIdMap>(email).GetAccountID();
            if (string.IsNullOrEmpty(playerId))
                throw new RequestFailedException(GatewayErrors.Refused, UnknownEmail);
            await SignIn.WithInputErrorsAsync(() => context.Grains.GetGrain<IPlayer>(playerId).Activate(request.code));
            return new EmptyMessage();
        }
    }
}
