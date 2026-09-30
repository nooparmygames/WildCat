using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WildCat.Interfaces
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

    [GenerateSerializer]
    [Alias("wildcat.ItemState")]
    public class ItemState
    {
        [Id(0)] public bool IsCreated { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public string Description { get; set; }
        [Id(3)] public Dictionary<string, string> MetaData { get; set; }
        [Id(4)] public int MaxAllowedToHave { get; set; }
        [Id(5)] public bool Stackable { get; set; }
        [Id(6)] public bool AutomaticallyConsummed { get; set; }
        [Id(7)] public TimeSpan EffectTime { get; set; }
        [Id(8)] public bool IsConsumable { get; set; }
        [Id(9)] public bool IsRegenerating { get; set; }
        [Id(10)] public int RegenerationAmount { get; set; }
        [Id(11)] public TimeSpan RegenerationPeriod { get; set; }
        [Id(12)] public List<PlayerInventoryState.InInventoryItemState> Price { get; set; }

        public ItemState()
        {
            MetaData = new Dictionary<string, string>();
        }
    }
}
