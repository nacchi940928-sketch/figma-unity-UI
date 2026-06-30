using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Figma Import re-flows children when parent layoutMode != NONE, ignoring baked x/y.
    /// Break auto-layout on parents whose children no longer match flow positions.
    /// </summary>
    public static class AutoLayoutExportPatcher
    {
        const float Epsilon = 0.51f;

        public sealed class NormalizeResult
        {
            public int BrokenCount;
            public List<string> BrokenIrIds = new List<string>();
        }

        public static NormalizeResult NormalizeTree(JObject rootNode)
        {
            var result = new NormalizeResult();
            if (rootNode != null)
                NormalizeNode(rootNode, result);
            return result;
        }

        static void NormalizeNode(JObject node, NormalizeResult result)
        {
            var children = node["children"] as JArray;

            if (children != null)
            {
                foreach (var childToken in children)
                {
                    if (childToken is JObject child)
                        NormalizeNode(child, result);
                }
            }

            if (!ShouldBreakAutoLayout(node))
                return;

            if (node["layout"] is JObject layout)
                layout["layoutMode"] = "NONE";

            var irId = node.Value<string>("irId");
            if (!string.IsNullOrEmpty(irId))
                result.BrokenIrIds.Add(irId);

            if (children != null)
            {
                foreach (var childToken in children)
                {
                    if (childToken is not JObject child)
                        continue;

                    child["layoutPositioning"] = "ABSOLUTE";
                    if (string.Equals(child.Value<string>("type"), "TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        child["layoutSizingHorizontal"] = "FIXED";
                        child["layoutSizingVertical"] = "FIXED";
                    }
                }
            }

            result.BrokenCount++;
        }

        static bool ShouldBreakAutoLayout(JObject node)
        {
            if (node["layout"] is not JObject layout)
                return false;

            var mode = layout.Value<string>("layoutMode");
            if (string.IsNullOrEmpty(mode)
                || string.Equals(mode, "NONE", StringComparison.OrdinalIgnoreCase))
                return false;

            if (node["children"] is not JArray children || children.Count == 0)
                return false;

            return string.Equals(mode, "VERTICAL", StringComparison.OrdinalIgnoreCase)
                ? !MatchesVerticalFlow(node, layout, children)
                : string.Equals(mode, "HORIZONTAL", StringComparison.OrdinalIgnoreCase)
                    && !MatchesHorizontalFlow(node, layout, children);
        }

        static bool MatchesVerticalFlow(JObject node, JObject layout, JArray children)
        {
            var parentWidth = node.Value<float?>("width") ?? 0f;
            var paddingTop = layout.Value<float?>("paddingTop") ?? 0f;
            var paddingLeft = layout.Value<float?>("paddingLeft") ?? 0f;
            var paddingRight = layout.Value<float?>("paddingRight") ?? 0f;
            var itemSpacing = layout.Value<float?>("itemSpacing") ?? 0f;
            var counterAlign = layout.Value<string>("counterAxisAlignItems") ?? "MIN";
            var innerWidth = Math.Max(parentWidth - paddingLeft - paddingRight, 0f);
            var currentY = paddingTop;

            foreach (var childToken in children)
            {
                if (childToken is not JObject child)
                    continue;

                var childWidth = child.Value<float?>("width") ?? 0f;
                var childHeight = child.Value<float?>("height") ?? 0f;
                var expectedX = ResolveCounterAxisX(paddingLeft, innerWidth, childWidth, counterAlign, paddingRight, parentWidth);
                var expectedY = currentY;
                var actualX = child.Value<float?>("x") ?? 0f;
                var actualY = child.Value<float?>("y") ?? 0f;

                if (!NearlyEqual(actualX, expectedX) || !NearlyEqual(actualY, expectedY))
                    return false;

                currentY += childHeight + itemSpacing;
            }

            return true;
        }

        static bool MatchesHorizontalFlow(JObject node, JObject layout, JArray children)
        {
            var parentHeight = node.Value<float?>("height") ?? 0f;
            var paddingLeft = layout.Value<float?>("paddingLeft") ?? 0f;
            var paddingTop = layout.Value<float?>("paddingTop") ?? 0f;
            var paddingBottom = layout.Value<float?>("paddingBottom") ?? 0f;
            var itemSpacing = layout.Value<float?>("itemSpacing") ?? 0f;
            var counterAlign = layout.Value<string>("counterAxisAlignItems") ?? "MIN";
            var innerHeight = Math.Max(parentHeight - paddingTop - paddingBottom, 0f);
            var currentX = paddingLeft;

            foreach (var childToken in children)
            {
                if (childToken is not JObject child)
                    continue;

                var childWidth = child.Value<float?>("width") ?? 0f;
                var childHeight = child.Value<float?>("height") ?? 0f;
                var expectedX = currentX;
                var expectedY = ResolveCounterAxisY(paddingTop, innerHeight, childHeight, counterAlign, paddingBottom, parentHeight);
                var actualX = child.Value<float?>("x") ?? 0f;
                var actualY = child.Value<float?>("y") ?? 0f;

                if (!NearlyEqual(actualX, expectedX) || !NearlyEqual(actualY, expectedY))
                    return false;

                currentX += childWidth + itemSpacing;
            }

            return true;
        }

        static float ResolveCounterAxisX(
            float paddingLeft,
            float innerWidth,
            float childWidth,
            string counterAlign,
            float paddingRight,
            float parentWidth)
        {
            if (string.Equals(counterAlign, "CENTER", StringComparison.OrdinalIgnoreCase))
                return paddingLeft + Math.Max((innerWidth - childWidth) * 0.5f, 0f);

            if (string.Equals(counterAlign, "MAX", StringComparison.OrdinalIgnoreCase))
                return Math.Max(parentWidth - paddingRight - childWidth, paddingLeft);

            return paddingLeft;
        }

        static float ResolveCounterAxisY(
            float paddingTop,
            float innerHeight,
            float childHeight,
            string counterAlign,
            float paddingBottom,
            float parentHeight)
        {
            if (string.Equals(counterAlign, "CENTER", StringComparison.OrdinalIgnoreCase))
                return paddingTop + Math.Max((innerHeight - childHeight) * 0.5f, 0f);

            if (string.Equals(counterAlign, "MAX", StringComparison.OrdinalIgnoreCase))
                return Math.Max(parentHeight - paddingBottom - childHeight, paddingTop);

            return paddingTop;
        }

        static bool NearlyEqual(float a, float b)
        {
            return Math.Abs(a - b) <= Epsilon;
        }
    }
}
