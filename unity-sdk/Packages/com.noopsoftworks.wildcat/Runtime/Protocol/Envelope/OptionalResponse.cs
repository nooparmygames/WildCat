namespace WildCat.Protocol
{
    /// <summary>
    /// The envelope of every response and of most server events: a success flag, then either the
    /// error code and message or the payload itself. Error codes: 1 for a refused or failed
    /// request, 2 for "already exists" and similar, other values as each message documents, and
    /// -1 for an internal error (the message is then a generic text; details stay in the server
    /// log). A failed response reads the same whatever <typeparamref name="T"/> is, so a client can
    /// always read one as OptionalResponse&lt;EmptyMessage&gt;.
    /// </summary>
    public class OptionalResponse<T> : MessageBase where T : MessageBase, new()
    {
        public bool isSuccesful;
        public string errorMsg;
        public int errorCode;
        public T msg;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(isSuccesful);
            if (!isSuccesful)
            {
                writer.WriteInt32(errorCode);
                writer.WriteString(errorMsg);
            }
            else
            {
                writer.WriteMessage(msg);
            }
        }

        public override void Deserialize(WireReader reader)
        {
            isSuccesful = reader.ReadBool();
            if (!isSuccesful)
            {
                errorCode = reader.ReadInt32();
                errorMsg = reader.ReadString();
            }
            else
            {
                msg = reader.ReadMessage<T>();
            }
        }

        public void Succeed(T msg)
        {
            isSuccesful = true;
            this.msg = msg;
        }

        public void Fail(string errorMsg, int errorCode)
        {
            isSuccesful = false;
            this.errorCode = errorCode;
            this.errorMsg = errorMsg;
        }

        public static OptionalResponse<T> CreateSuccessful(T msg)
        {
            OptionalResponse<T> resp = new OptionalResponse<T>();
            resp.Succeed(msg);
            return resp;
        }

        public static OptionalResponse<T> CreateFailed(string errorMsg, int errorCode)
        {
            OptionalResponse<T> resp = new OptionalResponse<T>();
            resp.Fail(errorMsg, errorCode);
            return resp;
        }
    }
}
