using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;
using LoginType = WildCat.Protocol.LoginType;

namespace WildCat.Gateway
{
    /// <summary>
    /// CreateNewAccount (78 -> 79): creates a device, user name or email account without logging
    /// in, on the session whose ticket is in the payload, and answers with the account id. A
    /// sign-in attempt. Error code 2 when the device, user name or email is taken, 1 when the
    /// account cannot be created (for example an empty password or invalid characters).
    /// </summary>
    internal sealed class CreateNewAccountHandler : RequestHandler<CreateAccountRequest, StringMessage>
    {
        public const string AlreadyExists = "Already exists";
        public const string CreationFailed = "The account could not be created.";

        public override WildCatMsgType RequestType => WildCatMsgType.CreateNewAccountRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.CreateNewAccountResponse;
        public override SessionRequirement Session => SessionRequirement.None;
        public override bool IsSignInAttempt => true;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, CreateAccountRequest request)
        {
            await SignIn.TouchPayloadTicketAsync(context.Grains, request.ticket);
            var creator = context.Grains.GetGrain<IPlayerCreator>(0);
            AccountCreationResult result;
            switch (request.LoginType)
            {
                case LoginType.Device:
                    result = await creator.CreateAccountWithDevice(request.Id);
                    break;
                case LoginType.UserWithPassword:
                    result = await creator.CreateAccountWithUserAndPassword(request.Id, request.PasswordHash);
                    break;
                case LoginType.Email:
                    result = await creator.CreateAccountWithEmail(SignIn.NormalizeEmail(request.Id), request.PasswordHash);
                    break;
                default:
                    throw new RequestFailedException(GatewayErrors.Refused, SignIn.UnsupportedLoginType);
            }

            if (result.result == AdditionResult.AlreadyExisting)
                throw new RequestFailedException(GatewayErrors.AlreadyExists, AlreadyExists);
            // PlayerCreator reports bad input (an empty password, invalid characters) as a failure.
            if (result.result != AdditionResult.Success)
                throw new RequestFailedException(GatewayErrors.Refused, CreationFailed);
            return new StringMessage { value = result.Account };
        }
    }
}
