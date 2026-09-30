namespace WildCat.Protocol
{
    /// <summary>RemoveInventoryItem (115): an inventory name with the (item name, count) pairs to remove.</summary>
    public class RemoveItemInventoryRequestMessage : MessageBase
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
            items = InventoryItems.Read(reader);
        }
    }
}
