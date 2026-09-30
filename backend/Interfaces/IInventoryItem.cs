using System.Threading.Tasks;

namespace Interfaces
{
    public interface IInventoryItem : IManagedResource
    {
        Task<InventoryItemType> GetItemType();
        Task<string> GetItemName();
    }

    [GenerateSerializer, Alias("wildcat.InventoryItemType")]
    public enum InventoryItemType : byte
    {
        Item,
        Package
    }
}