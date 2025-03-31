using MD.Platform.Log;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MD.BRIDGE
{
    public class SettingsModel
    {
        public string ServerAddress { get; set; }

        public Dictionary<Product, string> ProductLogDirectories { get; set; }

        [JsonConverter(typeof(CultureInfoConverter))]
        public CultureInfo CultureInfo { get; set; }

        public Dictionary<Product, DateTimeOffset> ProductOffsets { get; set; }
    }

    public class CultureInfoConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var cultureInfo = value as CultureInfo;
            if (cultureInfo != null)
            {
                writer.WriteValue(cultureInfo.Name);
            }
            else
            {
                writer.WriteNull();
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                return new CultureInfo(reader.Value.ToString());
            }
            return CultureInfo.InvariantCulture;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(CultureInfo);
        }
    }
}
