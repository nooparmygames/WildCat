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

        /// <summary>
        /// Reads the pairs; each takes at least five bytes (an empty name and the count).
        /// <paramref name="maxCount"/> limits how many a request may carry.
        /// </summary>
        public static (string, int)[] Read(WireReader reader, int maxCount = int.MaxValue)
        {
            int count = reader.ReadCount(5, maxCount);
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
