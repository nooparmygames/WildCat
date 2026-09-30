using System;

namespace WildCat.Protocol
{
    /// <summary>The answer to GetInventoryUnlockTime (122): the time left until the inventory unlocks.</summary>
    public class GetInventoryUnlockTimeResponseMessage : MessageBase
    {
        public TimeSpan time;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteTimeSpan(time);
        }

        public override void Deserialize(WireReader reader)
        {
            time = reader.ReadTimeSpan();
        }
    }
}
