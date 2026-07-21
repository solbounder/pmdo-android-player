using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PMDO.Android
{
    /// <summary>Preserves the comma-separated Vector2 format written by FNA builds.</summary>
    internal sealed class MonoGameVector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                float[] values = ((string)reader.Value)
                    .Split(',')
                    .Select(value => float.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture))
                    .ToArray();
                if (values.Length == 2)
                    return new Vector2(values[0], values[1]);

                throw new JsonSerializationException("Expected a two-component Vector2 string.");
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                float[] values = JArray.Load(reader).Values<float>().ToArray();
                if (values.Length == 2)
                    return new Vector2(values[0], values[1]);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject value = JObject.Load(reader);
                return new Vector2(value.Value<float>("X"), value.Value<float>("Y"));
            }

            throw new JsonSerializationException("Unsupported Vector2 representation: " + reader.TokenType);
        }

        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer) =>
            writer.WriteValue(string.Format(CultureInfo.InvariantCulture, "{0}, {1}", value.X, value.Y));
    }
}
