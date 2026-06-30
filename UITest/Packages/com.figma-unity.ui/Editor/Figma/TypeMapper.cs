using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class TypeMapper
    {
        public static string MapIrType(FigmaNode node)
        {
            var figmaType = node.type ?? "FRAME";
            switch (figmaType)
            {
                case "TEXT":
                    return "text";
                case "VECTOR":
                    return "image";
                case "LINE":
                    return "frame";
                case "RECTANGLE":
                    if (HasImageFill(node))
                        return "image";
                    return "frame";
                case "FRAME":
                default:
                    return "frame";
            }
        }

        public static bool HasImageFill(FigmaNode node)
        {
            if (node.fills == null) return false;
            foreach (var fill in node.fills)
            {
                if (fill?.type == "IMAGE")
                    return true;
            }
            return false;
        }
    }
}
