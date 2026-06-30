using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    public static class RectTransformSerializedReader
    {
        public struct Data
        {
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;
        }

        public static Data Read(RectTransform rt)
        {
            var so = new SerializedObject(rt);
            so.Update();
            return new Data
            {
                anchorMin = so.FindProperty("m_AnchorMin").vector2Value,
                anchorMax = so.FindProperty("m_AnchorMax").vector2Value,
                pivot = so.FindProperty("m_Pivot").vector2Value,
                anchoredPosition = so.FindProperty("m_AnchoredPosition").vector2Value,
                sizeDelta = so.FindProperty("m_SizeDelta").vector2Value
            };
        }
    }
}
