namespace WildCat.Protocol
{
    /// <summary>
    /// The inventory contents (item name and count) in the answers to GetInventory (110),
    /// RemoveInventoryItem (116) and UnlockInventory (120).
    /// </summary>
    public class GetInventoryResponseMessage : MessageBase
    {
        public (string, int)[] items = new (string, int)[0];

        public override void Serialize(WireWriter writer)
        {
            InventoryItems.Write(writer, items);
        }

        public override void Deserialize(WireReader reader)
        {
            items = InventoryItems.Read(reader);
        }
    }
}
