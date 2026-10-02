using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// RecoverPassword (84 -> 85): emails a password reset code to an email login; the client
    /// sends it back with ResetPasswordWithCode (163). Needs a session, not a login. A sign-in
    /// attempt. Always answers success, whether or not the address belongs to an account, so the
    /// answer does not reveal which addresses are registered.
    /// </summary>
    internal sealed class RecoverPasswordHandler : RequestHandler<RecoverPasswordRequest, EmptyMessage>
    {
        private readonly ILogger<RecoverPasswordHandler> logger;

        public RecoverPasswordHandler(ILogger<RecoverPasswordHandler> logger)
        {
            this.logger = logger;
        }

        public override WildCatMsgType RequestType => WildCatMsgType.RecoverPasswordRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.RecoverPasswordResponse;
        public override SessionRequirement Session => SessionRequirement.Session;
        public override bool IsSignInAttempt => true;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, RecoverPasswordRequest request)
        {
            string email = SignIn.NormalizeEmail(request.email);
            if (email.Length == 0)
                return new EmptyMessage();
            string playerId = await context.Grains.GetGrain<IEmailToAccountIdMap>(email).GetAccountID();
            if (string.IsNullOrEmpty(playerId))
                return new EmptyMessage();
            try
            {
                await context.Grains.GetGrain<IPlayer>(playerId).SendPasswordChangeCodeToEmail(email);
            }
            catch (Exception ex)
            {
                // The answer stays the same, but the owner should know that mail is not going out.
                logger.LogError(ex, "Sending a password reset code to player {PlayerId} failed", playerId);
            }
            return new EmptyMessage();
        }
    }
}
