using System;
using FigmaUnity.UI.Editor.IR;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Maps full Figma paint stack from document JObject (preserves gradients/effects XML fields).
    /// </summary>
    public static class PaintMapper
    {
        public static void AttachFromDocument(IRNode irRoot, JObject figmaRoot)
        {
            if (irRoot == null || figmaRoot == null)
                return;

            AttachRecursive(irRoot, figmaRoot);
        }

        static void AttachRecursive(IRNode ir, JObject figmaNode)
        {
            ir.paint = MapPaint(figmaNode);

            if (ir.children == null || ir.children.Count == 0)
                return;

            if (figmaNode["children"] is not JArray figmaChildren)
                return;

            var byIrId = new System.Collections.Generic.Dictionary<string, JObject>(StringComparer.Ordinal);
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

        public static IRPaintData MapPaint(JObject figmaNode)
        {
            var paint = new IRPaintData();
            if (figmaNode == null)
                return paint;

            if (figmaNode["fills"] is JArray fills)
            {
                foreach (var token in fills)
                {
                    if (token is not JObject fillObj)
                        continue;

                    var fill = MapFill(fillObj);
                    if (fill != null)
                        paint.fills.Add(fill);
                }
            }

            paint.stroke = MapStroke(figmaNode);
            paint.strokeAlign = figmaNode.Value<string>("strokeAlign") ?? "INSIDE";

            if (figmaNode["effects"] is JArray effects)
            {
                foreach (var token in effects)
                {
                    if (token is not JObject effectObj)
                        continue;

                    var effect = MapEffect(effectObj);
                    if (effect != null)
                        paint.effects.Add(effect);
                }
            }

            return paint;
        }

        static IRPaintFill MapFill(JObject fillObj)
        {
            var type = fillObj.Value<string>("type");
            if (string.IsNullOrEmpty(type))
                return null;

            var fill = new IRPaintFill
            {
                type = type,
                color = fillObj.Value<string>("color"),
                opacity = ReadFloat(fillObj["opacity"], 1f),
                imageFile = fillObj.Value<string>("imageFile")
            };

            if (type.StartsWith("GRADIENT", StringComparison.OrdinalIgnoreCase)
                && fillObj["stops"] is JArray stops)
            {
                foreach (var stopToken in stops)
                {
                    if (stopToken is not JObject stopObj)
                        continue;

                    fill.stops.Add(new IRGradientStop
                    {
                        position = ReadFloat(stopObj["position"], 0f),
                        color = stopObj.Value<string>("color")
                    });
                }

                if (fillObj["transform"] is JObject transform)
                {
                    fill.transform = new IRGradientTransform
                    {
                        a = ReadFloat(transform["a"], 1f),
                        b = ReadFloat(transform["b"], 0f),
                        c = ReadFloat(transform["c"], 0f),
                        d = ReadFloat(transform["d"], 1f),
                        tx = ReadFloat(transform["tx"], 0f),
                        ty = ReadFloat(transform["ty"], 0f)
                    };
                }
            }

            return fill;
        }

        static IRStroke MapStroke(JObject figmaNode)
        {
            var weight = ReadFloat(figmaNode["strokeWeight"], 0f);
            if (weight <= 0f)
                return null;

            if (figmaNode["strokes"] is not JArray strokes || strokes.Count == 0)
                return null;

            if (strokes[0] is not JObject strokeObj)
                return null;

            return new IRStroke
            {
                color = StyleMapper.NormalizeHex(strokeObj.Value<string>("color")),
                width = weight,
                opacity = ReadFloat(strokeObj["opacity"], 1f)
            };
        }

        static IREffect MapEffect(JObject effectObj)
        {
            var type = effectObj.Value<string>("type");
            if (!string.Equals(type, "DROP_SHADOW", StringComparison.OrdinalIgnoreCase))
                return null;

            if (effectObj.Value<bool?>("visible") == false)
                return null;

            var effect = new IREffect
            {
                type = type,
                visible = true,
                color = effectObj.Value<string>("color"),
                radius = ReadFloat(effectObj["radius"], 0f),
                spread = ReadFloat(effectObj["spread"], 0f)
            };

            if (effectObj["offset"] is JObject offset)
            {
                effect.offsetX = ReadFloat(offset["x"], 0f);
                effect.offsetY = ReadFloat(offset["y"], 0f);
            }

            return effect;
        }

        static float ReadFloat(JToken token, float fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();

            if (float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return fallback;
        }
    }
}
