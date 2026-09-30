using System;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Turns an event from a player's ChallengeEvents stream into the frame pushed to the client:
    /// StartChallengeRequestedEvent (19), ChallengeAcceptedEvent (22, with the new match id in
    /// GameKey) or ChallengeDeniedEvent (25), each an OptionalResponse&lt;ChallengeMessage&gt;.
    /// </summary>
    internal static class ChallengeEventFrames
    {
        public static WildCatMessageBase Build(ChallengeEventData data, string otherPlayerName)
        {
            var challenge = new ChallengeMessage
            {
                Player = data.otherPlayer?.GetPrimaryKeyString() ?? "",
                PlayerName = otherPlayerName ?? "",
                GameKey = Guid.Empty,
                data = data.data,
            };
            WildCatMsgType type;
            switch (data.Type)
            {
                case ChallengeEventData.EventType.Started:
                    challenge.GameKey = data.gameID;
                    type = WildCatMsgType.ChallengeAcceptedEvent;
                    break;
                case ChallengeEventData.EventType.Deny:
                    type = WildCatMsgType.ChallengeDeniedEvent;
                    break;
                case ChallengeEventData.EventType.Request:
                    type = WildCatMsgType.StartChallengeRequestedEvent;
                    break;
                default:
                    throw new ArgumentException($"{data.Type} is not a challenge event type.", nameof(data));
            }
            return WildCatMessageBase.Create(OptionalResponse<ChallengeMessage>.CreateSuccessful(challenge), type,
                WildCatMsgHeader.normal);
        }
    }
}
