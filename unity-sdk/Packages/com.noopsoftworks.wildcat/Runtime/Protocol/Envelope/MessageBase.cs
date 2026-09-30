namespace WildCat.Protocol
{
    /// <summary>
    /// Base class of every payload carried in a <see cref="WildCatMessageBase"/>. A payload writes
    /// its fields in <see cref="Serialize"/> and reads them back in the same order in
    /// <see cref="Deserialize"/>; <see cref="WireCodec"/> calls both. Game code derives its own
    /// payloads from it for the messages it relays between players.
    /// </summary>
    public class MessageBase
    {
        public virtual void Serialize(WireWriter writer)
        {
        }

        public virtual void Deserialize(WireReader reader)
        {
        }
    }
}
