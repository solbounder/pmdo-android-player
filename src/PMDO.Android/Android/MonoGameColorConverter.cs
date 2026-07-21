using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PMDO.Android
{
    /// <summary>Preserves the comma-separated Color format written by FNA builds.</summary>
    internal sealed class MonoGameColorConverter : JsonConverter<Color>
    {
        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                byte[] values = ((string)reader.Value)
                    .Split(',')
                    .Select(value => byte.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture))
                    .ToArray();
                if (values.Length == 3) return new Color(values[0], values[1], values[2]);
                if (values.Length == 4) return new Color(values[0], values[1], values[2], values[3]);
                throw new JsonSerializationException("Expected an RGB or RGBA color string.");
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                byte[] values = JArray.Load(reader).Values<byte>().ToArray();
                if (values.Length == 3) return new Color(values[0], values[1], values[2]);
                if (values.Length == 4) return new Color(values[0], values[1], values[2], values[3]);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject value = JObject.Load(reader);
                return new Color(value.Value<byte>("R"), value.Value<byte>("G"), value.Value<byte>("B"), value.Value<byte?>("A") ?? byte.MaxValue);
            }

            throw new JsonSerializationException("Unsupported Color representation: " + reader.TokenType);
        }

        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer) =>
            writer.WriteValue(string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}, {3}", value.R, value.G, value.B, value.A));
    }
}
