using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Detects child coordinates that are likely absolute values written as parent-relative coords.
    /// </summary>
    public static class FigmaLayoutBoundsValidator
    {
        const float Margin = 1f;

        public static List<string> ValidateDocument(JObject root)
        {
            var warnings = new List<string>();
            if (root?["node"] is JObject node)
                ValidateNode(node, null, warnings);
            return warnings;
        }

        static void ValidateNode(JObject node, JObject parent, List<string> warnings)
        {
            if (parent != null && IsOutOfParentBounds(node, parent, out var detail))
            {
                var name = node.Value<string>("name") ?? node.Value<string>("irId") ?? "(unnamed)";
                var parentName = parent.Value<string>("name") ?? parent.Value<string>("irId") ?? "(unnamed)";
                warnings.Add($"子节点「{name}」超出父节点「{parentName}」范围：{detail}");
            }

            if (node["children"] is not JArray children)
                return;

            foreach (var childToken in children)
            {
                if (childToken is JObject child)
                    ValidateNode(child, node, warnings);
            }
        }

        static bool IsOutOfParentBounds(JObject node, JObject parent, out string detail)
        {
            var x = node.Value<float?>("x") ?? 0f;
            var y = node.Value<float?>("y") ?? 0f;
            var width = node.Value<float?>("width") ?? 0f;
            var height = node.Value<float?>("height") ?? 0f;
            var scaleX = node.Value<float?>("scaleX") ?? 1f;
            var scaleY = node.Value<float?>("scaleY") ?? 1f;
            var effectiveWidth = width * scaleX;
            var effectiveHeight = height * scaleY;
            var parentWidth = parent.Value<float?>("width") ?? 0f;
            var parentHeight = parent.Value<float?>("height") ?? 0f;

            detail =
                $"child(x={x}, y={y}, w={width}, h={height}, scale=({scaleX},{scaleY}), " +
                $"effective(w={effectiveWidth}, h={effectiveHeight}) vs parent(w={parentWidth}, h={parentHeight})";

            if (parentWidth <= 0f && parentHeight <= 0f)
                return false;

            var exceedsX = parentWidth > 0f && (x > parentWidth + Margin || x + effectiveWidth > parentWidth + Margin);
            var exceedsY = parentHeight > 0f && (y > parentHeight + Margin || y + effectiveHeight > parentHeight + Margin);
            return exceedsX || exceedsY;
        }
    }
}
