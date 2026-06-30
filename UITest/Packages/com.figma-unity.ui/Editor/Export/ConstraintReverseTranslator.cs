using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    public static class ConstraintReverseTranslator
    {
        public static void ReadConstraints(RectTransform rt, out string horizontal, out string vertical)
        {
            ReadConstraints(RectTransformSerializedReader.Read(rt), out horizontal, out vertical);
        }

        public static void ReadConstraints(
            RectTransformSerializedReader.Data data,
            out string horizontal,
            out string vertical)
        {
            var stretchH = !Mathf.Approximately(data.anchorMin.x, data.anchorMax.x);
            var stretchV = !Mathf.Approximately(data.anchorMin.y, data.anchorMax.y);

            horizontal = stretchH
                ? "STRETCH"
                : MapHorizontal((data.anchorMin.x + data.anchorMax.x) * 0.5f);

            vertical = stretchV
                ? "STRETCH"
                : MapVertical((data.anchorMin.y + data.anchorMax.y) * 0.5f);
        }

        static string MapHorizontal(float anchorX)
        {
            if (Mathf.Approximately(anchorX, 0.5f))
                return "CENTER";
            if (Mathf.Approximately(anchorX, 1f))
                return "MAX";
            return "MIN";
        }

        static string MapVertical(float anchorY)
        {
            if (Mathf.Approximately(anchorY, 0.5f))
                return "CENTER";
            if (Mathf.Approximately(anchorY, 0f))
                return "MAX";
            return "MIN";
        }
    }
}
