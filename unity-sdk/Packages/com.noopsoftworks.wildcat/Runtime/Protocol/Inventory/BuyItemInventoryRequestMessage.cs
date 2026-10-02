namespace WildCat.Protocol
{
    /// <summary>
    /// An inventory name with (item name, count) pairs: AddItemToInventory (111) and
    /// SetItemToInventory (113), which the gateway refuses from clients, and BuyItem (117), which
    /// has no handler.
    /// </summary>
    public class BuyItemInventoryRequestMessage : MessageBase
    {
        public string inventoryName = "";
        public (string, int)[] items = new (string, int)[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(inventoryName);
            InventoryItems.Write(writer, items);
        }

        public override void Deserialize(WireReader reader)
        {
            inventoryName = reader.ReadString();
            items = InventoryItems.Read(reader, WireLimits.MaxInventoryItemsPerRequest);
        }
    }
}
