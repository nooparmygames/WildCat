namespace WildCat.Protocol
{
    /// <summary>Reads and writes the (item name, count) lists the inventory messages share.</summary>
    internal static class InventoryItems
    {
        public static void Write(WireWriter writer, (string, int)[] items)
        {
            int count = items == null ? 0 : items.Length;
            writer.WriteCount(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteString(items[i].Item1);
                writer.WriteInt32(items[i].Item2);
            }
        }

        public static (string, int)[] Read(WireReader reader)
        {
            int count = reader.ReadCount();
            (string, int)[] items = new (string, int)[count];
            for (int i = 0; i < count; ++i)
            {
                items[i].Item1 = reader.ReadString();
                items[i].Item2 = reader.ReadInt32();
            }
            return items;
        }
    }
}
