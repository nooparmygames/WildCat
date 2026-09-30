namespace WildCat.Protocol
{
    /// <summary>
    /// GetFriends (88): the public data keys to return with each friend. (Reading used to write
    /// into an empty array, so any non-empty list threw.)
    /// </summary>
    public class GetFriendsRequestMessage : MessageBase
    {
        public string[] publicKeys = new string[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteStringArray(publicKeys);
        }

        public override void Deserialize(WireReader reader)
        {
            publicKeys = reader.ReadStringArray();
        }
    }
}
