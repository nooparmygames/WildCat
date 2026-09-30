using System;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.Runtime;

namespace Collections
{
    using Orleans;
    using Orleans.Providers;

    public class OrleansJsonSerializer
    {
        public const string UseFullAssemblyNamesProperty = "UseFullAssemblyNames";
        public const string IndentJsonProperty = "IndentJSON";
        public const string TypeNameHandlingProperty = "TypeNameHandling";
        private readonly Lazy<JsonSerializerSettings> settings;

        public OrleansJsonSerializer(IGrainFactory grainFactory)
        {
            this.settings = new Lazy<JsonSerializerSettings>(() =>
            {
                return GetDefaultSerializerSettings(grainFactory);
            });
        }

        /// <summary>
        /// Returns the default serializer settings.
        /// </summary>
        /// <returns>The default serializer settings.</returns>
        public static JsonSerializerSettings GetDefaultSerializerSettings(IGrainFactory grainFactory)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                Formatting = Formatting.None,
            };

            settings.Converters.Add(new IPAddressConverter());
            settings.Converters.Add(new IPEndPointConverter());
            settings.Converters.Add(new SiloAddressConverter());
            settings.Converters.Add(new GrainReferenceConverter(grainFactory));

            return settings;
        }

        public static JsonSerializerSettings UpdateSerializerSettings(JsonSerializerSettings settings, bool useFullAssemblyNames, bool indentJson, TypeNameHandling? typeNameHandling)
        {
            if (useFullAssemblyNames)
            {
                settings.TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full;
            }

            if (indentJson)
            {
                settings.Formatting = Formatting.Indented;
            }

            if (typeNameHandling.HasValue)
            {
                settings.TypeNameHandling = typeNameHandling.Value;
            }

            return settings;
        }

        /// <inheritdoc />
        public bool IsSupportedType(Type itemType)
        {
            return true;
        }
    }

    public class IPAddressConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPAddress));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            IPAddress ip = (IPAddress)value;
            writer.WriteValue(ip.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            return IPAddress.Parse(token.Value<string>());
        }
    }

    public class SiloAddressConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(SiloAddress));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            SiloAddress addr = (SiloAddress)value;
            writer.WriteStartObject();
            writer.WritePropertyName("SiloAddress");
            writer.WriteValue(addr.ToParsableString());
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            SiloAddress addr = SiloAddress.FromParsableString(jo["SiloAddress"].ToObject<string>());
            return addr;
        }
    }

    public class IPEndPointConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPEndPoint));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            IPEndPoint ep = (IPEndPoint)value;
            writer.WriteStartObject();
            writer.WritePropertyName("Address");
            serializer.Serialize(writer, ep.Address);
            writer.WritePropertyName("Port");
            writer.WriteValue(ep.Port);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            IPAddress address = jo["Address"].ToObject<IPAddress>(serializer);
            int port = jo["Port"].Value<int>();
            return new IPEndPoint(address, port);
        }
    }

    public class GrainReferenceConverter : JsonConverter
    {
        private static readonly Type AddressableType = typeof(IAddressable);
        private readonly IGrainFactory grainFactory;
        private readonly JsonSerializer internalSerializer;

        public GrainReferenceConverter(IGrainFactory grainFactory)
        {
            this.grainFactory = grainFactory;

            // Create a serializer for internal serialization which does not have a specified GrainReference serializer.
            // This internal serializer will use GrainReference's ISerializable implementation for serialization and deserialization.
            this.internalSerializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                PreserveReferencesHandling = PreserveReferencesHandling.None,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                Formatting = Formatting.None,
                Converters =
                {
                    new IPAddressConverter(),
                    new IPEndPointConverter(),
                    new SiloAddressConverter(),
                }
            });
        }

        public override bool CanConvert(Type objectType)
        {
            return AddressableType.IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Serialize the grain reference using the internal serializer.
            this.internalSerializer.Serialize(writer, value);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Deserialize using the internal serializer which will use the concrete GrainReference implementation's
            // ISerializable constructor.
            var result = this.internalSerializer.Deserialize(reader, objectType);
            var grainRef = result as IAddressable;
            if (grainRef == null) return result;

            // Bind the deserialized grain reference to the runtime.
            this.grainFactory.BindGrainReference(grainRef);
            return grainRef;
        }
    }
}
