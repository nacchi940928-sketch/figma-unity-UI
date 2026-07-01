using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Exports Unity RectTransform lossyScale as Figma scaleX/scaleY and adjusts x/y for pivot offset.
    /// Figma Import multiplies width/height by scale; x/y must reference the unscaled frame box.
    /// </summary>
    public static class ScaleExportHelper
    {
        public const float ScaleTolerance = 0.0001f;

        public static void ExportLayout(
            UnityNodePatch patch,
            RectTransform rt,
            RectTransform parentRt,
            RectTransform prefabRootRt)
        {
            if (patch == null || rt == null)
                return;

            var origWidth = rt.rect.width;
            var origHeight = rt.rect.height;

            CoordReverseTranslator.ReadRectFromTransform(rt, parentRt, out var x, out var y, out var width, out var height);
            if (origWidth > 0.01f)
                width = origWidth;
            if (origHeight > 0.01f)
                height = origHeight;

            var lossy = rt.lossyScale;
            patch.scaleX = lossy.x;
            patch.scaleY = lossy.y;

            ApplyPivotCorrection(rt, width, height, patch.scaleX, patch.scaleY, ref x, ref y);
            patch.x = x;
            patch.y = y;
            patch.width = width;
            patch.height = height;

            if (prefabRootRt == null || rt == prefabRootRt)
            {
                patch.rootX = x;
                patch.rootY = y;
                return;
            }

            CoordReverseTranslator.ReadRectFromTransform(rt, prefabRootRt, out var rootX, out var rootY, out _, out _);
            ApplyPivotCorrection(rt, width, height, patch.scaleX, patch.scaleY, ref rootX, ref rootY);
            patch.rootX = rootX;
            patch.rootY = rootY;
        }

        public static void ApplyPivotCorrection(
            RectTransform rt,
            float origWidth,
            float origHeight,
            float scaleX,
            float scaleY,
            ref float x,
            ref float y)
        {
            if (NearlyOne(scaleX) && NearlyOne(scaleY))
                return;

            var pivotX = rt.pivot.x;
            var pivotY = 1f - rt.pivot.y;

            x += origWidth * pivotX * (1f - scaleX);
            y += origHeight * pivotY * (1f - scaleY);
        }

        public static bool ScalesDiffer(float? templateScale, float patchScale)
        {
            return System.Math.Abs((templateScale ?? 1f) - patchScale) > ScaleTolerance;
        }

        static bool NearlyOne(float value)
        {
            return System.Math.Abs(value - 1f) <= ScaleTolerance;
        }
    }
}
