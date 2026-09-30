using System;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// InitializeSession (6 -> 7): opens a session for a game of a developer and answers with its
    /// ticket. Needs no session, of course. Error code 1 when the developer does not exist or the
    /// game is not one of theirs.
    /// </summary>
    internal sealed class InitializeSessionHandler : RequestHandler<InitializeSessionRequest, GuidMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.InitializeSessionRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.InitializeSessionResponse;
        public override SessionRequirement Session => SessionRequirement.None;

        protected override async Task<GuidMessage> HandleAsync(MessageContext context, InitializeSessionRequest request)
        {
            Guid session;
            try
            {
                session = await context.Grains.GetGrain<IDeveloper>(request.DeveloperKey).InitializeSession(request.GameKey);
            }
            catch (InvalidOperationException)
            {
                // Thrown for an unknown developer and for a game that is not the developer's.
                throw new RequestFailedException(GatewayErrors.Refused, $"The game key {request.GameKey} is not valid.");
            }
            // The request carried no ticket, so nothing touched the new session yet.
            context.Grains.GetGrain<ISession>(session).Touch().Ignore();
            return new GuidMessage { value = session };
        }
    }
}
