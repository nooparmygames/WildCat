using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPublisherData (34 -> 35): a publisher value of the session's developer for a key, ""
    /// when the key has none. Game clients read these without a secret; the developer writes them
    /// with SetPublisherData_S (62), from the admin API or from script events.
    /// </summary>
    internal sealed class GetPublisherDataHandler : RequestHandler<StringMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPublisherDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPublisherDataResponse;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            string value = await context.Grains.GetGrain<IDeveloperStorage>(context.Session.DeveloperKey).GetString(request.value);
            return new StringMessage { value = value };
        }
    }
}
