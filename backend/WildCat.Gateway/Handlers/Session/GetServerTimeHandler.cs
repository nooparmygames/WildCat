using System;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetServerTime (42 -> 43): the server's clock in UTC, as a DateTimeMessage (it was a
    /// culture dependent string before step 6). Needs no session; a ticket in the header is not
    /// checked.
    /// </summary>
    internal sealed class GetServerTimeHandler : RequestHandler<EmptyMessage, DateTimeMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetServerTimeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetServerTimeResponse;
        public override SessionRequirement Session => SessionRequirement.None;

        protected override async Task<DateTimeMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            DateTime now = await context.Grains.GetGrain<ITimeService>(Guid.Empty).GetServerTime();
            return new DateTimeMessage { value = now };
        }
    }
}
