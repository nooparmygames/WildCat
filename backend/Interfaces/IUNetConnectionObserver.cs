using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    [System.Serializable]
    [GenerateSerializer]
    [Alias("wildcat.UNetConnectionDataTransferMessage")]
    public class UNetConnectionDataTransferMessage
    {
        [Id(0)] public EventType eventType;
        [Id(1)] public IPlayer player;
        [Id(2)] public int connectionId;
        [Id(3)] public int channelId;
        [Id(4)] public byte[] data;
        [Id(5)] public string gameKey;
        [Id(6)] public Guid matchId;
        [GenerateSerializer, Alias("wildcat.UNetConnectionDataTransferMessage.EventType")]
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