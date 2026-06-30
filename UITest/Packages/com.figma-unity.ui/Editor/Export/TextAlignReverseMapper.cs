using TMPro;

namespace FigmaUnity.UI.Editor.Export
{
    public static class TextAlignReverseMapper
    {
        public static void FromTmp(TextMeshProUGUI tmp, out string horizontal, out string vertical)
        {
            horizontal = MapHorizontal(tmp.horizontalAlignment);
            vertical = MapVertical(tmp.verticalAlignment);
        }

        static string MapHorizontal(HorizontalAlignmentOptions alignment)
        {
            return alignment switch
            {
                HorizontalAlignmentOptions.Justified => "JUSTIFIED",
                HorizontalAlignmentOptions.Right => "RIGHT",
                HorizontalAlignmentOptions.Center => "CENTER",
                _ => "LEFT"
            };
        }

        static string MapVertical(VerticalAlignmentOptions alignment)
        {
            return alignment switch
            {
                VerticalAlignmentOptions.Bottom => "BOTTOM",
                VerticalAlignmentOptions.Middle => "CENTER",
                _ => "TOP"
            };
        }
    }
}
