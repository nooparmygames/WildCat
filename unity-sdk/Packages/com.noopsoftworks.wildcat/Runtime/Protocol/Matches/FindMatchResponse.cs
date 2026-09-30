using System;

namespace WildCat.Protocol
{
    /// <summary>The answer to FindMatch (16): the match id and its players.</summary>
    public class FindMatchResponse : MessageBase
    {
        public Guid gameId = Guid.Empty;
        public string data = "";
        public string[] players = new string[0];

        public FindMatchResponse()
        {
        }

        /// <summary>
        /// (This constructor used to take the match id as a string and drop it, so the id was
        /// always empty.)
        /// </summary>
        public FindMatchResponse(Guid gameId, string data, string[] players)
        {
            this.gameId = gameId;
            this.data = data;
            this.players = players;
        }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(gameId);
            writer.WriteString(data);
            writer.WriteStringArray(players);
        }

        public override void Deserialize(WireReader reader)
        {
            gameId = reader.ReadGuid();
            data = reader.ReadString();
            players = reader.ReadStringArray();
        }
    }
}
