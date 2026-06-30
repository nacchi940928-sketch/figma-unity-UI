using System;
using Newtonsoft.Json;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Figma 可能导出 "mixed" 等非整数字符串（如 fontWeight）。
    /// </summary>
    public class FlexibleIntConverter : JsonConverter<int>
    {
        public override int ReadJson(JsonReader reader, Type objectType, int existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    return Convert.ToInt32(reader.Value);
                case JsonToken.String:
                    var text = reader.Value?.ToString();
                    if (string.IsNullOrEmpty(text) || text.Equals("mixed", StringComparison.OrdinalIgnoreCase))
                        return 0;
                    return int.TryParse(text, out var parsed) ? parsed : 0;
                case JsonToken.Float:
                    return Convert.ToInt32(reader.Value);
                case JsonToken.Null:
                    return 0;
                default:
                    return 0;
            }
        }

        public override void WriteJson(JsonWriter writer, int value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }
}
