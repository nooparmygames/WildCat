using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPublisherData_S (64 -> 65): server to server, reads a publisher value ("" when the key
    /// has none). The developer key and secret in the payload are the credentials, so no session is
    /// needed (a ticket in the header is not checked). Error code 1 for an unknown developer or a
    /// wrong secret; before step 4 the secret was not checked at all (bug 4).
    /// </summary>
    internal sealed class GetPublisherDataServerHandler : RequestHandler<GetPublisherDataRequest, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPublisherDataRequest_S;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPublisherDataResponse_S;
        public override SessionRequirement Session => SessionRequirement.None;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, GetPublisherDataRequest request)
        {
            var storage = context.Grains.GetGrain<IDeveloperStorage>(request.developerKey);
            string value = await DeveloperSecret.CheckedAsync(() => storage.GetStringForDeveloper(request.key, request.developerSecret));
            return new StringMessage { value = value };
        }
    }
}
