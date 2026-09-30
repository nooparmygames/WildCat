using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPlayerChallengeHandler : IGrainWithGuidCompoundKey
    {
        Task<ChallengeAcceptionStatus> ChallengeRequested(IPlayer challenger, byte[] data, bool isAcceptedOffline);
        Task<ChallengeAcceptionStatus> SendChallengeRequest(IPlayer challengee, byte[] data, uint timeout, bool isAcceptedOffline);
        Task AcceptChallenge(IPlayer accepter, byte[] data);
        Task DenyChallenge(IPlayer accepter, byte[] data);
        Task<IRelayServerGame> CreateGame(IPlayer[] players, string gameName);
        Task RemovedFromReceivedList(IPlayer other);
        Task UpdateInReceiveList(IPlayer other, Guid gameKey);
        Task<(List<ChallengeRequestState> sent, List<ChallengeRequestState> received)> GetChallengeRequests();
    }

    public enum ChallengeAcceptionStatus : byte
    {
        Received,
        Disconnected,
        RequestedYouAsWell
    }

    [Serializable]
    public struct ChallengeEventData
    {
        public enum EventType { Request, Started, Deny }
        public IPlayer otherPlayer { get; set; }
        public EventType Type { get; set; }
        public byte[] data { get; set; }
        public Guid gameID { get; set; }
        public Guid TitleKey { get; set; }

        public override string ToString()
        {
            return $"{Type} {data} {otherPlayer} {gameID}";
        }
    }

    [Serializable]
    public class ChallengeTimeoutException : Exception
    {
        public ChallengeTimeoutException()
        {

        }

        public ChallengeTimeoutException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext ctx) : base(info, ctx)
        {

        }
    }

    [Serializable]
    public class PlayerChallengesState
    {
        public List<ChallengeRequestState> RequestsSent { get; set; }
        public List<ChallengeRequestState> RequestsReceived { get; set; }

        public PlayerChallengesState()
        {
            RequestsSent = new List<ChallengeRequestState>();
            RequestsReceived = new List<ChallengeRequestState>();
        }
    }

    [Serializable]
    public struct ChallengeRequestState
    {
        public enum Status:byte { Pending, Started, Finished }
        public Status State { get; set; }
        public IPlayer Player { get; set; }
        public uint Timeout { get; set; }
        public byte[] data;
        public DateTime RequestTime { get; set; }
        public Guid Game { get; set; }
    }
}
