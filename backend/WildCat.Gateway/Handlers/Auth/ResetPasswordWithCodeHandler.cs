using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// ResetPasswordWithCode (163 -> 164): sets a new password for an email login with the code
    /// RecoverPassword (84) sent, which also activates the address. Needs a session, not a login.
    /// A sign-in attempt. Error code 1 for an unacceptable password and, with the same text for
    /// both, for an unknown address and a wrong, used or expired code.
    /// </summary>
    internal sealed class ResetPasswordWithCodeHandler : RequestHandler<ResetPasswordWithCodeRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.ResetPasswordWithCodeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.ResetPasswordWithCodeResponse;
        public override SessionRequirement Session => SessionRequirement.Session;
        public override bool IsSignInAttempt => true;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, ResetPasswordWithCodeRequest request)
        {
            // Checked first so a bad password does not use up the code.
            await SignIn.WithInputErrorsAsync(() =>
            {
                PasswordHasher.EnsureAcceptable(request.newPassword);
                return Task.FromResult(true);
            });

            string email = SignIn.NormalizeEmail(request.email);
            string playerId = email.Length == 0 ? null
                : await context.Grains.GetGrain<IEmailToAccountIdMap>(email).GetAccountID();
            if (string.IsNullOrEmpty(playerId))
                throw new RequestFailedException(GatewayErrors.Refused, SignIn.WrongOrExpiredCode);

            bool changed = await context.Grains.GetGrain<IPlayer>(playerId).ChangePasswordForEmailByCode(request.code, request.newPassword);
            if (!changed)
                throw new RequestFailedException(GatewayErrors.Refused, SignIn.WrongOrExpiredCode);
            return new EmptyMessage();
        }
    }
}
