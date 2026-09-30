using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Builds protocol payloads filled with sample data by reflection, and compares two payloads
    /// field by field. Every public instance field is filled except those marked [NonSerialized]
    /// (not sent on the wire).
    /// </summary>
    internal static class ProtocolSampleData
    {
        public static readonly DateTime SampleTime = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

        /// <summary>Every concrete payload type in the protocol assembly, nested ones included.</summary>
        public static IEnumerable<Type> PayloadTypes()
        {
            return typeof(MessageBase).Assembly.GetTypes()
                .Where(t => typeof(MessageBase).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
        }

        public static MessageBase CreatePayload(Type type)
        {
            // Payloads whose fields depend on each other.
            if (type == typeof(ScriptEventExecutionRequestMessage))
            {
                var request = new ScriptEventExecutionRequestMessage { eventName = "on-sample" };
                request.SetParametersAndTypes(true, (byte)7, (short)-8, 9, -10L, 1.5f, -2.25, "text é中", new byte[] { 1, 2 });
                return request;
            }
            if (type == typeof(ScriptEventExecutionResponseMessage))
                return new ScriptEventExecutionResponseMessage { hasReturnValue = true, returnType = ParamType.StringParam, returnValue = "returned" };
            return (MessageBase)Create(type, type.Name);
        }

        public static object Create(Type type, string path)
        {
            if (type == typeof(string))
                return $"{path} é中\U0001F600";
            if (type == typeof(bool))
                return true;
            if (type == typeof(byte))
                return (byte)200;
            if (type == typeof(short))
                return (short)-1234;
            if (type == typeof(int))
                return 123456789 + (path.Length % 7);
            if (type == typeof(uint))
                return 4000000000u;
            if (type == typeof(long))
                return -9876543210L;
            if (type == typeof(float))
                return 1.5f;
            if (type == typeof(double))
                return -2.25;
            if (type == typeof(Guid))
                return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(path)));
            if (type == typeof(DateTime))
                return SampleTime;
            if (type == typeof(TimeSpan))
                return TimeSpan.FromMilliseconds(90061001);
            if (type == typeof(byte[]))
                return new byte[] { 0, 1, 2, 127, 128, 250, 255 };
            if (type.IsEnum)
                return Enum.GetValues(type).GetValue(0);
            if (type == typeof((string, int)))
                return ($"item {path}", 42);
            if (type.IsArray)
            {
                var element = type.GetElementType();
                var array = Array.CreateInstance(element, 2);
                for (int i = 0; i < 2; ++i)
                    array.SetValue(Create(element, $"{path}[{i}]"), i);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < 2; ++i)
                    list.Add(Create(type.GetGenericArguments()[0], $"{path}[{i}]"));
                return list;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var map = (IDictionary)Activator.CreateInstance(type);
                var valueType = type.GetGenericArguments()[1];
                for (int i = 0; i < 2; ++i)
                    map.Add($"{path} key {i}", Create(valueType, $"{path}[{i}]"));
                return map;
            }
            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) != null)
            {
                object instance = Activator.CreateInstance(type);
                foreach (var field in WireFields(type))
                    field.SetValue(instance, Create(field.FieldType, $"{path}.{field.Name}"));
                return instance;
            }
            throw new NotSupportedException($"No sample data for {type} at {path}; add it to {nameof(ProtocolSampleData)}.");
        }

        /// <summary>The fields a payload puts on the wire: public, instance, not [NonSerialized].</summary>
        public static IEnumerable<FieldInfo> WireFields(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsDefined(typeof(NonSerializedAttribute), false) && !f.IsInitOnly);
        }

        /// <summary>Asserts that two values are equal field by field, with <paramref name="path"/> in the failure message.</summary>
        public static void AssertDeepEqual(object expected, object actual, string path)
        {
            if (expected == null || actual == null)
            {
                Assert.True(expected == null && actual == null, $"{path}: expected {expected ?? "null"}, got {actual ?? "null"}");
                return;
            }
            Type type = expected.GetType();
            Assert.True(type == actual.GetType(), $"{path}: expected a {type}, got a {actual.GetType()}");
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(TimeSpan))
            {
                Assert.True(Equals(expected, actual), $"{path}: expected {expected}, got {actual}");
            }
            else if (type == typeof(DateTime))
            {
                Assert.True(((DateTime)expected).Ticks == ((DateTime)actual).Ticks, $"{path}: expected {expected:o}, got {actual:o}");
                Assert.Equal(DateTimeKind.Utc, ((DateTime)actual).Kind);
            }
            else if (type == typeof((string, int)))
            {
                Assert.True(Equals(expected, actual), $"{path}: expected {expected}, got {actual}");
            }
            else if (expected is IDictionary expectedMap)
            {
                var actualMap = (IDictionary)actual;
                Assert.True(expectedMap.Count == actualMap.Count, $"{path}: expected {expectedMap.Count} entries, got {actualMap.Count}");
                foreach (DictionaryEntry entry in expectedMap)
                {
                    Assert.True(actualMap.Contains(entry.Key), $"{path}: key {entry.Key} is missing");
                    AssertDeepEqual(entry.Value, actualMap[entry.Key], $"{path}[{entry.Key}]");
                }
            }
            else if (expected is IList expectedList)
            {
                var actualList = (IList)actual;
                Assert.True(expectedList.Count == actualList.Count, $"{path}: expected {expectedList.Count} items, got {actualList.Count}");
                for (int i = 0; i < expectedList.Count; ++i)
                    AssertDeepEqual(expectedList[i], actualList[i], $"{path}[{i}]");
            }
            else
            {
                foreach (var field in WireFields(type))
                    AssertDeepEqual(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}");
            }
        }

        /// <summary>Calls WireCodec.DecodePayload&lt;T&gt; for a runtime type.</summary>
        public static MessageBase Decode(Type type, byte[] bytes, out int consumed)
        {
            var method = typeof(WireCodec).GetMethods()
                .Single(m => m.Name == nameof(WireCodec.DecodePayload) && m.GetParameters().Length == 2)
                .MakeGenericMethod(type);
            var arguments = new object[] { bytes, 0 };
            try
            {
                var result = (MessageBase)method.Invoke(null, arguments);
                consumed = (int)arguments[1];
                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
