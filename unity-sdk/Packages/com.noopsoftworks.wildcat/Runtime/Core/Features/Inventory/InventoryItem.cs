namespace WildCat
{
    /// <summary>An item and how many of it an inventory holds (or a change asks for).</summary>
    public struct InventoryItem
    {
        public InventoryItem(string name, int count)
        {
            Name = name;
            Count = count;
        }

        /// <summary>The item's name in the game's item catalog.</summary>
        public string Name { get; }

        public int Count { get; }

        public override string ToString()
        {
            return Name + " x" + Count;
        }
    }
}
