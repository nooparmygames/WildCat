using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
    public interface IItem : IInventoryItem
    {
        Task Initialize(string name, string description, Dictionary<string, string> metaData, int maxAllowed, bool stackable, bool automaticallyConsummed, TimeSpan effectTime, bool isConsummable, bool isRegenerating, int regenerationAmount, TimeSpan regenerationPeriod,List<PlayerInventoryState.InInventoryItemState> price);
        Task<ItemState> GetItemInfo();
        Task SetItemProperties(string name, string description, Dictionary<string, string> metaData, int maxAllowed, bool stackable, bool automaticallyConsummed, TimeSpan effectTime, bool isConsummed, bool isRegenerating, int regenerationAmount, TimeSpan regenerationPeriod,List<PlayerInventoryState.InInventoryItemState> price);
        Task SetName(string name);
        Task SetDescription(string description);
        
        Task<string> GetDescription();
    }

    public class ItemState
    {
        public bool IsCreated { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> MetaData { get; set; }
        public int MaxAllowedToHave { get; set; }
        public bool Stackable { get; set; }
        public bool AutomaticallyConsummed { get; set; }
        public TimeSpan EffectTime { get; set; }
        public bool IsConsumable { get; set; }
        public bool IsRegenerating { get; set; }
        public int RegenerationAmount { get; set; }
        public TimeSpan RegenerationPeriod { get; set; }
        public List<PlayerInventoryState.InInventoryItemState> Price { get; set; }

        public ItemState()
        {
            MetaData = new Dictionary<string, string>();
        }
    }
}
