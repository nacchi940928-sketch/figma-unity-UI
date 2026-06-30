using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    public static class CoordReverseTranslator
    {
        /// <summary>
        /// Reads baked Figma x/y/width/height from the resolved RectTransform geometry.
        /// Use after PrepareForExport so LayoutGroup sizes are applied.
        /// </summary>
        public static void ReadRectFromTransform(
            RectTransform rt,
            RectTransform parentRt,
            out float x,
            out float y,
            out float width,
            out float height)
        {
            width = ResolveAxisSize(rt, true);
            height = ResolveAxisSize(rt, false);

            if (parentRt == null)
            {
                x = 0f;
                y = 0f;
                return;
            }

            var childTopLeft = rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMax, 0f));
            var parentTopLeft = parentRt.TransformPoint(new Vector3(parentRt.rect.xMin, parentRt.rect.yMax, 0f));

            var childInParent = parentRt.InverseTransformPoint(childTopLeft);
            var parentOriginInParent = parentRt.InverseTransformPoint(parentTopLeft);

            x = childInParent.x - parentOriginInParent.x;
            y = parentOriginInParent.y - childInParent.y;
        }

        static float ResolveAxisSize(RectTransform rt, bool horizontal)
        {
            var rectSize = horizontal ? rt.rect.width : rt.rect.height;
            if (rectSize > 0.01f)
                return rectSize;

            var sizeDelta = horizontal ? rt.sizeDelta.x : rt.sizeDelta.y;
            return Mathf.Abs(sizeDelta);
        }

        public static void ReadRect(
            RectTransform rt,
            RectTransform parentRt,
            out float x,
            out float y,
            out float width,
            out float height)
        {
            var data = RectTransformSerializedReader.Read(rt);
            ReadRect(data, parentRt, out x, out y, out width, out height);
        }

        public static void ReadRect(
            RectTransformSerializedReader.Data data,
            RectTransform parentRt,
            out float x,
            out float y,
            out float width,
            out float height)
        {
            RectTransformSerializedReader.Data? parentData = null;
            if (parentRt != null)
                parentData = RectTransformSerializedReader.Read(parentRt);

            ReadRect(data, parentData, out x, out y, out width, out height);
        }

        public static void ReadRect(
            RectTransformSerializedReader.Data data,
            RectTransformSerializedReader.Data? parentData,
            out float x,
            out float y,
            out float width,
            out float height)
        {
            width = GetAxisSize(data, true);
            height = GetAxisSize(data, false);

            if (parentData == null)
            {
                x = 0f;
                y = 0f;
                return;
            }

            var parentWidth = GetAxisSize(parentData.Value, true);
            var parentHeight = GetAxisSize(parentData.Value, false);

            var stretchH = IsStretch(data, true);
            var stretchV = IsStretch(data, false);

            if (stretchH && stretchV)
            {
                ReadStretchOffsets(data, parentWidth, parentHeight, out x, out y, out width, out height);
                return;
            }

            if (stretchH)
            {
                x = ReadStretchX(data, parentWidth, width);
                ReadPointAnchorY(data, parentHeight, height, out y);
                return;
            }

            if (stretchV)
            {
                y = ReadStretchY(data, parentHeight, height);
                ReadPointAnchorX(data, parentWidth, width, out x);
                return;
            }

            ReadPointAnchor(data, parentWidth, parentHeight, width, height, out x, out y);
        }

        static void ReadPointAnchor(
            RectTransformSerializedReader.Data data,
            float parentWidth,
            float parentHeight,
            float width,
            float height,
            out float x,
            out float y)
        {
            var anchorX = (data.anchorMin.x + data.anchorMax.x) * 0.5f;
            var anchorY = (data.anchorMin.y + data.anchorMax.y) * 0.5f;

            var pivotWorldX = data.anchoredPosition.x + parentWidth * anchorX;
            var pivotFromParentTopY = parentHeight * (1f - anchorY) - data.anchoredPosition.y;

            x = pivotWorldX - width * data.pivot.x;
            y = pivotFromParentTopY - height * (1f - data.pivot.y);
        }

        static void ReadPointAnchorX(RectTransformSerializedReader.Data data, float parentWidth, float width, out float x)
        {
            var anchorX = (data.anchorMin.x + data.anchorMax.x) * 0.5f;
            var pivotWorldX = data.anchoredPosition.x + parentWidth * anchorX;
            x = pivotWorldX - width * data.pivot.x;
        }

        static void ReadPointAnchorY(RectTransformSerializedReader.Data data, float parentHeight, float height, out float y)
        {
            var anchorY = (data.anchorMin.y + data.anchorMax.y) * 0.5f;
            var pivotFromParentTopY = parentHeight * (1f - anchorY) - data.anchoredPosition.y;
            y = pivotFromParentTopY - height * (1f - data.pivot.y);
        }

        static void ReadStretchOffsets(
            RectTransformSerializedReader.Data data,
            float parentWidth,
            float parentHeight,
            out float x,
            out float y,
            out float width,
            out float height)
        {
            // Inverse of ConstraintTranslator.ApplyStretch (both axes).
            // Use sizeDelta as initial size estimate, then solve for final width/height.
            var guessWidth = Mathf.Max(Mathf.Abs(data.sizeDelta.x), 1f);
            var guessHeight = Mathf.Max(Mathf.Abs(data.sizeDelta.y), 1f);

            x = data.anchoredPosition.x - data.anchorMin.x * parentWidth + data.pivot.x * guessWidth;
            var right = (1f - data.anchorMax.x) * parentWidth - data.anchoredPosition.x
                + (1f - data.pivot.x) * guessWidth;
            y = (1f - data.anchorMax.y) * parentHeight - data.anchoredPosition.y
                + (1f - data.pivot.y) * guessHeight;
            var bottom = data.anchoredPosition.y - data.anchorMin.y * parentHeight + data.pivot.y * guessHeight;

            width = parentWidth - x - right;
            height = parentHeight - y - bottom;
        }

        static float ReadStretchX(RectTransformSerializedReader.Data data, float parentWidth, float width)
        {
            return data.anchoredPosition.x - data.anchorMin.x * parentWidth + data.pivot.x * width;
        }

        static float ReadStretchY(RectTransformSerializedReader.Data data, float parentHeight, float height)
        {
            return (1f - data.anchorMax.y) * parentHeight - data.anchoredPosition.y + (1f - data.pivot.y) * height;
        }

        static bool IsStretch(RectTransformSerializedReader.Data data, bool horizontal)
        {
            return horizontal
                ? !Mathf.Approximately(data.anchorMin.x, data.anchorMax.x)
                : !Mathf.Approximately(data.anchorMin.y, data.anchorMax.y);
        }

        static float GetAxisSize(RectTransformSerializedReader.Data data, bool horizontal)
        {
            if (IsStretch(data, horizontal))
            {
                // Stretch sizes need parent context; caller handles stretch branch.
                return horizontal ? Mathf.Abs(data.sizeDelta.x) : Mathf.Abs(data.sizeDelta.y);
            }

            var sizeDelta = horizontal ? data.sizeDelta.x : data.sizeDelta.y;
            return Mathf.Abs(sizeDelta);
        }

        public static void PrepareForExport(RectTransform root)
        {
            if (root == null)
                return;

            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }
    }
}
