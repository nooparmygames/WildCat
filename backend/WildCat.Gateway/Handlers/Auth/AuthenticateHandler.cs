using System;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;
using LoginType = WildCat.Protocol.LoginType;

namespace WildCat.Gateway
{
    /// <summary>
    /// Authenticate (76 -> 77): logs a player in on the session whose ticket is in the payload,
    /// creating a device or user name account first when AutoCreate asks for it, and binds the
    /// login to this connection. A sign-in attempt. Error code 1 for a wrong password, an unknown
    /// or inactive login, and bad input.
    /// </summary>
    internal sealed class AuthenticateHandler : RequestHandler<AuthenticationRequest, AuthenticationResponse>
    {
        private readonly LoginManager logins;

        public AuthenticateHandler(LoginManager logins)
        {
            this.logins = logins;
        }

        public override WildCatMsgType RequestType => WildCatMsgType.AuthenticateRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.AuthenticateResponse;
        public override SessionRequirement Session => SessionRequirement.None;
        public override bool IsSignInAttempt => true;

        protected override async Task<AuthenticationResponse> HandleAsync(MessageContext context, AuthenticationRequest request)
        {
            IGrainFactory grains = context.Grains;
            SignIn.CheckLoginId(request.LoginType, request.Id);
            Guid sessionKey = await SignIn.TouchPayloadTicketAsync(grains, request.ticket);

            string account = await FindAccountAsync(grains, request.LoginType, request.Id);
            bool isNewlyCreated = false;
            if (string.IsNullOrEmpty(account))
            {
                account = await CreateAccountAsync(grains, request);
                isNewlyCreated = true;
            }

            // Identifies this login to the player grain. The connection keeps it only once the
            // login succeeds, so a failed attempt cannot replace the ticket of the login the
            // socket already holds.
            Guid loginTicket = Guid.NewGuid();
            IPlayer player = grains.GetGrain<IPlayer>(account);
            LoginResult login = await SignIn.WithInputErrorsAsync(() => LoginAsync(player, request, sessionKey, loginTicket));
            if (login == null)
                throw new RequestFailedException(GatewayErrors.Refused, SignIn.LoginFailed);

            Guid gameKey = (await grains.GetGrain<ISession>(sessionKey).GetDeveloperKey()).GameKey;
            await logins.BindAsync(context.Connection, player, gameKey, loginTicket);
            return new AuthenticationResponse
            {
                name = login.name,
                userId = login.userID,
                ticket = login.ticket,
                isNewlyCreated = isNewlyCreated,
            };
        }

        private static Task<string> FindAccountAsync(IGrainFactory grains, LoginType type, string id)
        {
            switch (type)
            {
                case LoginType.Device:
                    return grains.GetGrain<ICustomIDToAccountIDMap>(UserMappingUtility.GetDeviceIdMappingId(id)).GetAccountID();
                case LoginType.UserWithPassword:
                    return grains.GetGrain<IUserIdToAccountIdMap>(id ?? "").GetAccountID();
                case LoginType.Email:
                    return grains.GetGrain<IEmailToAccountIdMap>(SignIn.NormalizeEmail(id)).GetAccountID();
                default:
                    throw new RequestFailedException(GatewayErrors.Refused, SignIn.UnsupportedLoginType);
            }
        }

        /// <summary>Creates the account for a login that does not exist yet, when the request allows it.</summary>
        private static async Task<string> CreateAccountAsync(IGrainFactory grains, AuthenticationRequest request)
        {
            AccountCreationResult created = null;
            if (request.AutoCreate && request.LoginType == LoginType.Device)
                created = await grains.GetGrain<IPlayerCreator>(0).CreateAccountWithDevice(request.Id);
            else if (request.AutoCreate && request.LoginType == LoginType.UserWithPassword)
                created = await grains.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(request.Id, request.PasswordHash);

            if (created == null || created.result != AdditionResult.Success)
                throw new RequestFailedException(GatewayErrors.Refused, SignIn.LoginFailed);
            return created.Account;
        }

        private static Task<LoginResult> LoginAsync(IPlayer player, AuthenticationRequest request, Guid sessionKey, Guid loginTicket)
        {
            switch (request.LoginType)
            {
                case LoginType.Device:
                    return player.LoginWithDeviceID(request.Id, sessionKey, loginTicket);
                case LoginType.UserWithPassword:
                    return player.LoginWithUserNameAndPassword(request.Id, request.PasswordHash, sessionKey, loginTicket);
                case LoginType.Email:
                    return player.LoginWithEmail(SignIn.NormalizeEmail(request.Id), request.PasswordHash, sessionKey, loginTicket);
                default:
                    throw new RequestFailedException(GatewayErrors.Refused, SignIn.UnsupportedLoginType);
            }
        }
    }
}
