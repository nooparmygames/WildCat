using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetPublisherData_S (62 -> 63): server to server, stores a publisher value that game clients
    /// read with GetPublisherData (34). The developer key and secret in the payload are the
    /// credentials, so no session is needed (a ticket in the header is not checked). Error code 1
    /// for an unknown developer or a wrong secret.
    /// </summary>
    internal sealed class SetPublisherDataServerHandler : RequestHandler<SetPublisherDataRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetPublisherDataRequest_S;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetPublisherDataResponse_S;
        public override SessionRequirement Session => SessionRequirement.None;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetPublisherDataRequest request)
        {
            var storage = context.Grains.GetGrain<IDeveloperStorage>(request.developerKey);
            await DeveloperSecret.CheckedAsync(() => storage.SetString(request.key, request.value, request.developerSecret));
            return new EmptyMessage();
        }
    }
}
