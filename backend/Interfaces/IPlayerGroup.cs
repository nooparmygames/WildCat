using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPlayerGroup:IGrainWithGuidCompoundKey
    {
        Task AddPlayer(string player);
        Task RemovePlayer(string player);
        Task<string[]> GetPlayers();
        Task SetLeader(string leader);
        Task InvitePlayer(string player);
        Task AcceptInvitation(Guid invitation);
        Task DenyInvitation(Guid invitation);
        Task SendMessageToAll(byte[] message);
        Task SendMessageToAllExceptOne(string player);
    }

    public class PlayerGroupState
    {
        public List<IPlayer> Players { get; set; }
        public IPlayer leader { get; set; }
    }

    public class InvitationData
    {
        public IPlayerGroup group { get; set; }
        public IPlayer Inviter { get; set; }
        public string InviterName { get; set; }
        public DateTime Date { get; set; }
    }
}
