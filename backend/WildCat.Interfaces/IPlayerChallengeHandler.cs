using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IPlayerChallengeHandler : IGrainWithGuidCompoundKey
    {
        /// <summary>
        /// Called on the challengee's handler by the challenger's <see cref="SendChallengeRequest"/>:
        /// records the challenge, open for <paramref name="timeout"/> seconds, and pushes it.
        /// </summary>
        Task<ChallengeAcceptionStatus> ChallengeRequested(IPlayer challenger, byte[] data, uint timeout, bool isAcceptedOffline);
        Task<ChallengeAcceptionStatus> SendChallengeRequest(IPlayer challengee, byte[] data, uint timeout, bool isAcceptedOffline);
        Task AcceptChallenge(IPlayer accepter, byte[] data);
        Task DenyChallenge(IPlayer accepter, byte[] data);
        Task<IRelayServerGame> CreateGame(IPlayer[] players, string gameName);
        Task RemovedFromReceivedList(IPlayer other);
        Task UpdateInReceiveList(IPlayer other, Guid gameKey);
        Task<(List<ChallengeRequestState> sent, List<ChallengeRequestState> received)> GetChallengeRequests();
    }

    [GenerateSerializer, Alias("wildcat.ChallengeAcceptionStatus")]
    public enum ChallengeAcceptionStatus : byte
    {
        Received,
        Disconnected,
        RequestedYouAsWell
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.ChallengeEventData")]
    public struct ChallengeEventData
    {
        [GenerateSerializer, Alias("wildcat.ChallengeEventData.EventType")]
        public enum EventType { Request, Started, Deny }
        [Id(0)] public IPlayer otherPlayer { get; set; }
        [Id(1)] public EventType Type { get; set; }
        [Id(2)] public byte[] data { get; set; }
        [Id(3)] public Guid gameID { get; set; }
        [Id(4)] public Guid TitleKey { get; set; }

        /// <summary>How long the challenge stays open, in seconds, as the challenger asked.</summary>
        [Id(5)] public uint Timeout { get; set; }

        public override string ToString()
        {
            return $"{Type} {data} {otherPlayer} {gameID}";
        }
    }

    /// <summary>
    /// Thrown by <see cref="IPlayerChallengeHandler.AcceptChallenge"/> when the challenge's
    /// timeout has passed. The gateway answers it with error code 1 and its message.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.ChallengeTimeoutException")]
    public class ChallengeTimeoutException : Exception
    {
        public const string DefaultMessage = "The challenge has timed out.";

        public ChallengeTimeoutException() : base(DefaultMessage)
        {

        }

        public ChallengeTimeoutException(string message) : base(message)
        {

        }

        public ChallengeTimeoutException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.PlayerChallengesState")]
    public class PlayerChallengesState
    {
        [Id(0)] public List<ChallengeRequestState> RequestsSent { get; set; }
        [Id(1)] public List<ChallengeRequestState> RequestsReceived { get; set; }

        public PlayerChallengesState()
        {
            RequestsSent = new List<ChallengeRequestState>();
            RequestsReceived = new List<ChallengeRequestState>();
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.ChallengeRequestState")]
    public struct ChallengeRequestState
    {
        [GenerateSerializer, Alias("wildcat.ChallengeRequestState.Status")]
        public enum Status:byte { Pending, Started, Finished }
        [Id(0)] public Status State { get; set; }
        [Id(1)] public IPlayer Player { get; set; }
        [Id(2)] public uint Timeout { get; set; }
        [Id(3)] public byte[] data;
        [Id(4)] public DateTime RequestTime { get; set; }
        [Id(5)] public Guid Game { get; set; }
    }
}
