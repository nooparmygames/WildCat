using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    [System.Serializable]
    public class UNetConnectionDataTransferMessage
    {
        public EventType eventType;
        public IPlayer player;
        public int connectionId;
        public int channelId;
        public byte[] data;
        public string gameKey;
        public Guid matchId;
        public enum EventType
        {
            DataTransfer,
            Login,
            Logout,
            GameCreated,
            GameDestroyed
        }
    }
}