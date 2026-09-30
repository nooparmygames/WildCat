using System;
using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>The answer to GetPlayerChallenges (162): the challenges the player sent and received.</summary>
    public class ChallengeRequestsMessage : MessageBase
    {
        public class Request
        {
            public enum Status { Pending, Started, Finished }

            public Status status;
            public string Player = "";
            public string PlayerName = "";
            public Guid gameId;
            public byte[] data = new byte[0];
        }

        public List<Request> sent = new List<Request>();
        public List<Request> received = new List<Request>();

        public override void Serialize(WireWriter writer)
        {
            WriteRequests(writer, sent);
            WriteRequests(writer, received);
        }

        public override void Deserialize(WireReader reader)
        {
            sent = ReadRequests(reader);
            received = ReadRequests(reader);
        }

        private static void WriteRequests(WireWriter writer, List<Request> requests)
        {
            int count = requests == null ? 0 : requests.Count;
            writer.WriteCount(count);
            for (int i = 0; i < count; ++i)
            {
                Request r = requests[i] ?? new Request();
                writer.WriteByte((byte)r.status);
                writer.WriteGuid(r.gameId);
                writer.WriteString(r.Player);
                writer.WriteString(r.PlayerName);
                bool hasData = r.data != null && r.data.Length != 0;
                writer.WriteBool(hasData);
                if (hasData)
                    writer.WriteBytes(r.data);
            }
        }

        private static List<Request> ReadRequests(WireReader reader)
        {
            int count = reader.ReadCount();
            List<Request> requests = new List<Request>(count);
            for (int i = 0; i < count; ++i)
            {
                Request r = new Request();
                r.status = (Request.Status)reader.ReadByte();
                r.gameId = reader.ReadGuid();
                r.Player = reader.ReadString();
                r.PlayerName = reader.ReadString();
                r.data = reader.ReadBool() ? reader.ReadBytes() : new byte[0];
                requests.Add(r);
            }
            return requests;
        }
    }
}
