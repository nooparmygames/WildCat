using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>The logged in player's display name, and other players' names.</summary>
    public sealed class ProfileClient : FeatureClient
    {
        internal ProfileClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>Sets the logged in player's display name.</summary>
        public Task SetNameAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(name, "name");
            return Request<EmptyMessage>(WildCatMsgType.SetNameRequest, new StringMessage { value = name },
                WildCatMsgType.SetNameResponse, cancellationToken);
        }

        /// <summary>The logged in player's display name; empty when none was set.</summary>
        public async Task<string> GetNameAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            StringMessage answer = await Request<StringMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(),
                WildCatMsgType.GetNameResponse, cancellationToken).ConfigureAwait(false);
            return answer.value;
        }

        /// <summary>Another player's display name, by account id.</summary>
        public async Task<string> GetPlayerNameAsync(string playerId, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(playerId, "playerId");
            StringMessage answer = await Request<StringMessage>(WildCatMsgType.GetPlayerNameRequest, new StringMessage { value = playerId },
                WildCatMsgType.GetPlayerNameResponse, cancellationToken).ConfigureAwait(false);
            return answer.value;
        }
    }
}
