using System;
using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Applies TEXT alignment and layout (wrap, overflow, line spacing) from raw export JObject.
    /// </summary>
    public static class TextStyleDocumentMapper
    {
        public static void AttachFromDocument(IRNode irRoot, JObject figmaRoot)
        {
            if (irRoot == null || figmaRoot == null)
                return;

            AttachRecursive(irRoot, figmaRoot);
        }

        static void AttachRecursive(IRNode ir, JObject figma)
        {
            if (string.Equals(ir.type, "text", StringComparison.OrdinalIgnoreCase))
            {
                ir.text ??= new IRText();

                var h = figma.Value<string>("textAlignHorizontal");
                var v = figma.Value<string>("textAlignVertical");
                if (!string.IsNullOrWhiteSpace(h))
                    ir.text.align = StyleMapper.MapTextAlignHorizontal(h);
                if (!string.IsNullOrWhiteSpace(v))
                    ir.text.alignVertical = StyleMapper.MapTextAlignVertical(v);

                var autoResize = figma.Value<string>("textAutoResize");
                var maxLines = ReadMaxLines(figma["maxLines"]);
                StyleMapper.ApplyTextLayout(ir.text, autoResize, maxLines);

                var fontSize = ResolveFontSize(figma);
                if (fontSize > 0f)
                    ir.text.fontSize = fontSize;

                var characters = figma.Value<string>("characters");
                if (!string.IsNullOrEmpty(characters))
                    ir.text.content = characters.TrimEnd();

                if (figma["lineHeight"] is JObject lineHeight)
                    ir.text.lineSpacing = StyleMapper.MapLineSpacing(ir.text.fontSize, lineHeight);
            }

            if (ir.children == null || ir.children.Count == 0)
                return;

            if (figma["children"] is not JArray figmaChildren)
                return;

            var byIrId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var token in figmaChildren)
            {
                if (token is not JObject child)
                    continue;

                var irId = child.Value<string>("irId");
                if (!string.IsNullOrEmpty(irId))
                    byIrId[irId] = child;
            }

            foreach (var irChild in ir.children)
            {
                if (irChild == null || string.IsNullOrEmpty(irChild.id))
                    continue;

                if (byIrId.TryGetValue(irChild.id, out var figmaChild))
                    AttachRecursive(irChild, figmaChild);
            }
        }

        static int ReadMaxLines(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            if (token.Type == JTokenType.Float)
                return (int)token.Value<float>();

            return int.TryParse(token.ToString(), out var parsed) ? parsed : 0;
        }

        static float ResolveFontSize(JObject figma)
        {
            var token = figma["fontSize"];
            if (token == null || token.Type == JTokenType.Null)
                return ResolveFontSizeFromSegments(figma);

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                var direct = token.Value<float>();
                return direct > 0f ? direct : ResolveFontSizeFromSegments(figma);
            }

            if (token.Type == JTokenType.String)
            {
                var text = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(text)
                    && !string.Equals(text, "mixed", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    && parsed > 0f)
                    return parsed;
            }

            return ResolveFontSizeFromSegments(figma);
        }

        static float ResolveFontSizeFromSegments(JObject figma)
        {
            if (figma["segments"] is not JArray segments || segments.Count == 0)
                return 0f;

            var bestSize = 0f;
            var bestLen = 0;
            foreach (var token in segments)
            {
                if (token is not JObject segment)
                    continue;

                var size = ReadFloat(segment["fontSize"]);
                if (size <= 0f)
                    continue;

                var len = (segment.Value<string>("text") ?? string.Empty).Trim().Length;
                if (len >= bestLen)
                {
                    bestLen = len;
                    bestSize = size;
                }
            }

            if (bestSize > 0f)
                return bestSize;

            foreach (var token in segments)
            {
                if (token is not JObject segment)
                    continue;

                var size = ReadFloat(segment["fontSize"]);
                if (size > 0f)
                    return size;
            }

            return 0f;
        }

        static float ReadFloat(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0f;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();

            return float.TryParse(token.ToString(), out var parsed) ? parsed : 0f;
        }
    }
}
