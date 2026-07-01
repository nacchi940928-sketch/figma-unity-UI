using System;
using System.Globalization;
using Newtonsoft.Json;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Figma XML/JSON may omit optional numbers or export empty / "mixed" values.
    /// </summary>
    public class FlexibleFloatConverter : JsonConverter<float>
    {
        public override float ReadJson(
            JsonReader reader,
            Type objectType,
            float existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Float:
                case JsonToken.Integer:
                    return Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.String:
                    var text = reader.Value?.ToString();
                    if (string.IsNullOrWhiteSpace(text)
                        || text.Equals("mixed", StringComparison.OrdinalIgnoreCase))
                        return 0f;
                    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0f;
                case JsonToken.Null:
                    return 0f;
                default:
                    return 0f;
            }
        }

        public override void WriteJson(JsonWriter writer, float value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }
}
