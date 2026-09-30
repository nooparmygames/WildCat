using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// ExecuteScriptEvent (66): the event name and its typed arguments. Each argument is its
    /// <see cref="ParamType"/> as a byte followed by the value.
    /// </summary>
    public class ScriptEventExecutionRequestMessage : MessageBase
    {
        public string eventName = "";
        public ParamType[] parameterTypess = new ParamType[0];
        public object[] parameters = new object[0];

        /// <summary>
        /// Always true for now. The flag is on the wire so that untyped arguments can be added
        /// later; a false flag is followed by nothing.
        /// </summary>
        private bool isTypeSafe = true;

        public ScriptEventExecutionRequestMessage()
        {
        }

        public ScriptEventExecutionRequestMessage(string eventName, int parameterCount)
        {
            this.eventName = eventName;
            parameters = new object[parameterCount];
            parameterTypess = new ParamType[parameterCount];
        }

        public void SetParameter<T>(int index, T value)
        {
            if (index < 0 || index > (parameters.Length - 1))
                throw new ArgumentException("Index should be between 0 and paramterCount - 1");
            parameters[index] = value;
        }

        public void SetParameterType(int index, ParamType value)
        {
            if (index < 0 || index > (parameters.Length - 1))
                throw new ArgumentException("Index should be between 0 and paramterCount - 1");
            parameterTypess[index] = value;
        }

        /// <summary>Sets the arguments and works out their types (int, short, long, bool, byte, byte[], float, double, string).</summary>
        public void SetParametersAndTypes(params object[] args)
        {
            parameters = new object[args.Length];
            parameterTypess = new ParamType[args.Length];
            for (int i = 0; i < args.Length; ++i)
            {
                parameters[i] = args[i];
                parameterTypess[i] = ScriptValues.TypeOf(args[i]);
            }
        }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(isTypeSafe);
            if (!isTypeSafe)
                return;
            writer.WriteString(eventName);
            int count = parameterTypess == null ? 0 : parameterTypess.Length;
            if (count != (parameters == null ? 0 : parameters.Length))
                throw new InvalidOperationException("Every script argument needs a type.");
            writer.WriteCount(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteByte((byte)parameterTypess[i]);
                ScriptValues.Write(writer, parameterTypess[i], parameters[i]);
            }
        }

        public override void Deserialize(WireReader reader)
        {
            isTypeSafe = reader.ReadBool();
            if (!isTypeSafe)
                return;
            eventName = reader.ReadString();
            int count = reader.ReadCount();
            parameterTypess = new ParamType[count];
            parameters = new object[count];
            for (int i = 0; i < count; ++i)
            {
                parameterTypess[i] = (ParamType)reader.ReadByte();
                parameters[i] = ScriptValues.Read(reader, parameterTypess[i]);
            }
        }
    }
}
