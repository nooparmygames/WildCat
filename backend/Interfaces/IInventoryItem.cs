using System.Threading.Tasks;

namespace Interfaces
{
    public interface IInventoryItem : IManagedResource
    {
        Task<InventoryItemType> GetItemType();
        Task<string> GetItemName();
    }

    public enum InventoryItemType : byte
    {
        Item,
        Package
    }
}