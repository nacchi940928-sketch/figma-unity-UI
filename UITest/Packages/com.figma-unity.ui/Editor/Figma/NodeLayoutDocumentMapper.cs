using System;
using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Authoritative layout sync from raw export JObject (width/height/position).
    /// Ensures merge re-import always picks up Figma size changes.
    /// </summary>
    public static class NodeLayoutDocumentMapper
    {
        public static void AttachFromDocument(IRNode irRoot, JObject figmaRoot)
        {
            if (irRoot == null || figmaRoot == null)
                return;

            AttachRecursive(irRoot, figmaRoot, null, null);
        }

        static void AttachRecursive(
            IRNode ir,
            JObject figma,
            float? parentRootX,
            float? parentRootY)
        {
            if (figma == null)
                return;

            var width = ReadFloat(figma["width"]);
            var height = ReadFloat(figma["height"]);
            if (width > 0f)
                ir.width = width;
            if (height > 0f)
                ir.height = height;

            var scaleX = ReadFloat(figma["scaleX"], 1f);
            var scaleY = ReadFloat(figma["scaleY"], 1f);
            if (scaleX > 0f)
                ir.scaleX = scaleX;
            if (scaleY > 0f)
                ir.scaleY = scaleY;

            var nodeRootX = ReadFloat(figma["rootX"]);
            var nodeRootY = ReadFloat(figma["rootY"]);
            if (parentRootX.HasValue && parentRootY.HasValue)
            {
                ir.x = nodeRootX - parentRootX.Value;
                ir.y = nodeRootY - parentRootY.Value;
            }

            if (ir.children == null || ir.children.Count == 0)
                return;

            if (figma["children"] is not JArray figmaChildren)
                return;

            var byIrId = IndexChildren(figmaChildren);
            foreach (var irChild in ir.children)
            {
                if (irChild == null || string.IsNullOrEmpty(irChild.id))
                    continue;

                if (byIrId.TryGetValue(irChild.id, out var figmaChild))
                    AttachRecursive(irChild, figmaChild, nodeRootX, nodeRootY);
            }
        }

        static Dictionary<string, JObject> IndexChildren(JArray figmaChildren)
        {
            var byIrId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var token in figmaChildren)
            {
                if (token is not JObject child)
                    continue;

                var irId = child.Value<string>("irId");
                if (!string.IsNullOrEmpty(irId))
                    byIrId[irId] = child;
            }

            return byIrId;
        }

        static float ReadFloat(JToken token, float defaultValue = 0f)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();

            return float.TryParse(token.ToString(), out var parsed) ? parsed : defaultValue;
        }
    }
}
