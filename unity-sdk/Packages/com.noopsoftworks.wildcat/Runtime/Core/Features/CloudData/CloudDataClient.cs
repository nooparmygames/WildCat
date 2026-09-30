using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Per-player string key/value data on the server: private data only the player reads, public
    /// data every player can read, and the developer's publisher data (read only for clients).
    /// Keys are limited to 100 characters.
    /// </summary>
    public sealed class CloudDataClient : FeatureClient
    {
        internal CloudDataClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>Saves a private value of the logged in player.</summary>
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireKey(key);
            return Request<EmptyMessage>(WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = key, value = value ?? "" },
                WildCatMsgType.SetCloudDataResponse, cancellationToken);
        }

        /// <summary>Reads a private value of the logged in player; empty when the key was never set.</summary>
        public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireKey(key);
            StringMessage answer = await Request<StringMessage>(WildCatMsgType.GetCloudDataRequest, new StringMessage { value = key },
                WildCatMsgType.GetCloudDataResponse, cancellationToken).ConfigureAwait(false);
            return answer.value;
        }

        /// <summary>Saves a public value of the logged in player, which friends and other players can read.</summary>
        public Task SetPublicAsync(string key, string value, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireKey(key);
            return Request<EmptyMessage>(WildCatMsgType.SetPublicCloudDataRequest, new SetCloudDataRequest { key = key, value = value ?? "" },
                WildCatMsgType.SetPublicCloudDataResponse, cancellationToken);
        }

        /// <summary>Reads a public value of the logged in player; null when the key is not set.</summary>
        public async Task<string> GetPublicAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireKey(key);
            GetPublicCloudDataResponse answer = await Request<GetPublicCloudDataResponse>(WildCatMsgType.GetPublicCloudDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetPublicCloudDataResponse, cancellationToken).ConfigureAwait(false);
            return answer.hasValue ? answer.value : null;
        }

        /// <summary>Reads another player's public value; null when the key is not set.</summary>
        public async Task<string> GetPlayerPublicAsync(string playerId, string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(playerId, "playerId");
            RequireKey(key);
            GetPlayerPublicCloudDataResponse answer = await Request<GetPlayerPublicCloudDataResponse>(
                WildCatMsgType.GetPlayerPublicCloudDataRequest, new GetPlayerPublicCloudDataRequest { player = playerId, key = key },
                WildCatMsgType.GetPlayerPublicCloudDataResponse, cancellationToken).ConfigureAwait(false);
            return answer.hasValue ? answer.value : null;
        }

        /// <summary>
        /// Reads a value of the developer's publisher data (set from the admin panel or server
        /// tools; clients cannot write it). Empty when the key is not set.
        /// </summary>
        public async Task<string> GetPublisherDataAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireKey(key);
            StringMessage answer = await Request<StringMessage>(WildCatMsgType.GetPublisherDataRequest, new StringMessage { value = key },
                WildCatMsgType.GetPublisherDataResponse, cancellationToken).ConfigureAwait(false);
            return answer.value;
        }
    }
}
