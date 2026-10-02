using System.Collections.Generic;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>A friend in the friend list.</summary>
    public sealed class Friend
    {
        public Friend(string playerId, string name, OnlineStatus status, IReadOnlyDictionary<string, string> publicData)
        {
            PlayerId = playerId;
            Name = name;
            Status = status;
            PublicData = publicData ?? new Dictionary<string, string>();
        }

        public string PlayerId { get; }

        public string Name { get; }

        /// <summary>Online or offline, as of the answer; <see cref="FriendsClient.StatusChanged"/> reports changes.</summary>
        public OnlineStatus Status { get; }

        /// <summary>The friend's public cloud data for the keys the request asked for.</summary>
        public IReadOnlyDictionary<string, string> PublicData { get; }
    }
}
