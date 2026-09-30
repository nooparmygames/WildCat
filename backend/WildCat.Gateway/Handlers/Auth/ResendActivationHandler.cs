using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// ResendActivation (86 -> 87): emails a new activation code to an email login that is not
    /// activated yet (IPlayer.ResendActivationCode); the new code replaces the old one. Needs a
    /// session, not a login. A sign-in attempt. Always answers success, so the answer does not
    /// reveal which addresses are registered or activated.
    /// </summary>
    internal sealed class ResendActivationHandler : RequestHandler<ResendActivationRequest, EmptyMessage>
    {
        private readonly ILogger<ResendActivationHandler> logger;

        public ResendActivationHandler(ILogger<ResendActivationHandler> logger)
        {
            this.logger = logger;
        }

        public override WildCatMsgType RequestType => WildCatMsgType.ResendActivationRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.ResendActivationResponse;
        public override SessionRequirement Session => SessionRequirement.Session;
        public override bool IsSignInAttempt => true;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, ResendActivationRequest request)
        {
            string email = SignIn.NormalizeEmail(request.email);
            if (email.Length == 0)
                return new EmptyMessage();
            string playerId = await context.Grains.GetGrain<IEmailToAccountIdMap>(email).GetAccountID();
            if (string.IsNullOrEmpty(playerId))
                return new EmptyMessage();
            try
            {
                await context.Grains.GetGrain<IPlayer>(playerId).ResendActivationCode(email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sending a new activation code to player {PlayerId} failed", playerId);
            }
            return new EmptyMessage();
        }
    }
}
