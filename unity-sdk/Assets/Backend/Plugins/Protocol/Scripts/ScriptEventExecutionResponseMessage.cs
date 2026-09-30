using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// The answer to ExecuteScriptEvent (67): whether the script returned a value and, if so, its
    /// <see cref="ParamType"/> and the value.
    /// </summary>
    public class ScriptEventExecutionResponseMessage : MessageBase
    {
        public ParamType returnType;
        public bool hasReturnValue;
        public object returnValue;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(hasReturnValue);
            if (!hasReturnValue)
                return;
            writer.WriteByte((byte)returnType);
            ScriptValues.Write(writer, returnType, returnValue);
        }

        public override void Deserialize(WireReader reader)
        {
            hasReturnValue = reader.ReadBool();
            if (!hasReturnValue)
                return;
            returnType = (ParamType)reader.ReadByte();
            returnValue = ScriptValues.Read(reader, returnType);
        }

        /// <summary>The return value as a <typeparamref name="T"/>, which has to match <see cref="returnType"/>.</summary>
        public T GetValue<T>()
        {
            if (!hasReturnValue)
                throw new InvalidOperationException("The script response has no return type and you are trying to get the value of it");
            if (typeof(T) != ScriptValues.ClrTypeOf(returnType))
                throw new ArgumentException("The generic type doesn't match the type of the value");
            return (T)returnValue;
        }
    }
}
