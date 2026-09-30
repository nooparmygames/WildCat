using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;
using LoginType = WildCat.Protocol.LoginType;

namespace WildCat.Gateway
{
    /// <summary>
    /// AddAuthMechanismToAccount (82 -> 83): adds a device, user name or email login to the
    /// logged in player's account. A sign-in attempt. Error code 1 when the credential is taken
    /// ("Already exists") and for bad input; an email login gets an activation code by email.
    /// </summary>
    internal sealed class AddAuthMechanismToAccountHandler : RequestHandler<AddAuthenticationToAccountRequest, EmptyMessage>
    {
        public const string AlreadyExists = "Already exists";
        public const string AdditionFailed = "The login could not be added.";

        public override WildCatMsgType RequestType => WildCatMsgType.AddAuthMechanismToAccountRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.AddAuthMechanismToAccountResponse;
        public override bool IsSignInAttempt => true;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, AddAuthenticationToAccountRequest request)
        {
            IPlayer player = context.Player;
            AdditionResult addition;
            switch (request.LoginType)
            {
                case LoginType.Device:
                    addition = await SignIn.WithInputErrorsAsync(() => player.AddPlayerDevice(request.Id));
                    break;
                case LoginType.Email:
                    addition = await SignIn.WithInputErrorsAsync(() =>
                        player.AddPlayerEmail(SignIn.NormalizeEmail(request.Id), request.PasswordHash));
                    break;
                case LoginType.UserWithPassword:
                    addition = await SignIn.WithInputErrorsAsync(() => player.AddPlayerUserAndPassword(request.Id, request.PasswordHash));
                    break;
                default:
                    throw new RequestFailedException(GatewayErrors.Refused, SignIn.UnsupportedLoginType);
            }

            if (addition == AdditionResult.AlreadyExisting)
                throw new RequestFailedException(GatewayErrors.Refused, AlreadyExists);
            if (addition != AdditionResult.Success)
                throw new RequestFailedException(GatewayErrors.Refused, AdditionFailed);
            return new EmptyMessage();
        }
    }
}
