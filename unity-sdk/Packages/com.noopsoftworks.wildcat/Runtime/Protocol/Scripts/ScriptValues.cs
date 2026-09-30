using System;

namespace WildCat.Protocol
{
    /// <summary>Reads and writes the typed values of script event arguments and return values.</summary>
    internal static class ScriptValues
    {
        public static ParamType TypeOf(object value)
        {
            if (value == null)
                throw new ArgumentException("A script value cannot be null");
            Type t = value.GetType();
            if (t == typeof(int))
                return ParamType.IntParam;
            if (t == typeof(short))
                return ParamType.ShortParam;
            if (t == typeof(long))
                return ParamType.LongParam;
            if (t == typeof(bool))
                return ParamType.BoolParam;
            if (t == typeof(byte[]))
                return ParamType.ByteArrayParam;
            if (t == typeof(byte))
                return ParamType.ByteParam;
            if (t == typeof(float))
                return ParamType.FloatParam;
            if (t == typeof(double))
                return ParamType.DoubleParam;
            if (t == typeof(string))
                return ParamType.StringParam;
            throw new ArgumentException("Invalid argument type");
        }

        public static Type ClrTypeOf(ParamType type)
        {
            switch (type)
            {
                case ParamType.BoolParam: return typeof(bool);
                case ParamType.ByteArrayParam: return typeof(byte[]);
                case ParamType.ByteParam: return typeof(byte);
                case ParamType.DoubleParam: return typeof(double);
                case ParamType.FloatParam: return typeof(float);
                case ParamType.IntParam: return typeof(int);
                case ParamType.LongParam: return typeof(long);
                case ParamType.ShortParam: return typeof(short);
                case ParamType.StringParam: return typeof(string);
                default: throw new ArgumentException("Unknown script value type " + type);
            }
        }

        /// <summary>Writes <paramref name="value"/>, which must be of the type <paramref name="type"/> names.</summary>
        public static void Write(WireWriter writer, ParamType type, object value)
        {
            switch (type)
            {
                case ParamType.BoolParam:
                    writer.WriteBool((bool)value);
                    break;
                case ParamType.ByteArrayParam:
                    writer.WriteBytes((byte[])value);
                    break;
                case ParamType.ByteParam:
                    writer.WriteByte((byte)value);
                    break;
                case ParamType.DoubleParam:
                    writer.WriteDouble((double)value);
                    break;
                case ParamType.FloatParam:
                    writer.WriteSingle((float)value);
                    break;
                case ParamType.IntParam:
                    writer.WriteInt32((int)value);
                    break;
                case ParamType.LongParam:
                    writer.WriteInt64((long)value);
                    break;
                case ParamType.ShortParam:
                    writer.WriteInt16((short)value);
                    break;
                case ParamType.StringParam:
                    writer.WriteString((string)value);
                    break;
                default:
                    throw new ArgumentException("Unknown script value type " + type);
            }
        }

        public static object Read(WireReader reader, ParamType type)
        {
            switch (type)
            {
                case ParamType.BoolParam:
                    return reader.ReadBool();
                case ParamType.ByteArrayParam:
                    return reader.ReadBytes();
                case ParamType.ByteParam:
                    return reader.ReadByte();
                case ParamType.DoubleParam:
                    return reader.ReadDouble();
                case ParamType.FloatParam:
                    return reader.ReadSingle();
                case ParamType.IntParam:
                    return reader.ReadInt32();
                case ParamType.LongParam:
                    return reader.ReadInt64();
                case ParamType.ShortParam:
                    return reader.ReadInt16();
                case ParamType.StringParam:
                    return reader.ReadString();
                default:
                    throw new WireFormatException("Unknown script value type " + type);
            }
        }
    }
}
