using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class StyleMapper
    {
        public static IRConstraints MapConstraints(FigmaNode node)
        {
            if (node.constraints == null)
                return IRConstraints.Default;

            return new IRConstraints
            {
                horizontal = node.constraints.horizontal ?? "MIN",
                vertical = node.constraints.vertical ?? "MIN"
            };
        }

        public static List<IRFill> MapFills(FigmaNode node, string irType)
        {
            if (irType == "text")
                return new List<IRFill>();

            if (node.type == "LINE")
            {
                var stroke = GetFirstStroke(node);
                if (stroke == null)
                    return new List<IRFill>();
                return new List<IRFill>
                {
                    new IRFill
                    {
                        type = "solid",
                        color = NormalizeHex(stroke.color),
                        opacity = stroke.opacity
                    }
                };
            }

            if (node.fills == null || node.fills.Count == 0)
                return new List<IRFill>();

            var result = new List<IRFill>();
            foreach (var fill in node.fills)
            {
                if (fill == null || fill.type != "SOLID")
                    continue;
                result.Add(new IRFill
                {
                    type = "solid",
                    color = NormalizeHex(fill.color),
                    opacity = fill.opacity
                });
            }
            return result;
        }

        public static IRStroke MapStroke(FigmaNode node)
        {
            if (node.type == "LINE")
            {
                var s = GetFirstStroke(node);
                if (s == null) return null;
                return new IRStroke
                {
                    color = NormalizeHex(s.color),
                    width = node.strokeWeight > 0 ? node.strokeWeight : 1f,
                    opacity = s.opacity
                };
            }

            if (node.strokes == null || node.strokes.Count == 0 || node.strokeWeight <= 0)
                return null;

            var stroke = node.strokes[0];
            return new IRStroke
            {
                color = NormalizeHex(stroke.color),
                width = node.strokeWeight,
                opacity = stroke.opacity
            };
        }

        public static IRText MapText(FigmaNode node)
        {
            if (node.type != "TEXT")
                return null;

            var color = "#000000";
            if (node.fills != null && node.fills.Count > 0 && node.fills[0] != null)
                color = NormalizeHex(node.fills[0].color);

            var weight = ResolveFontWeight(node);
            var fontSize = node.fontSize > 0 ? node.fontSize : ResolveFontSize(node, 14f);

            return new IRText
            {
                content = node.characters ?? string.Empty,
                fontSize = fontSize,
                fontFamily = node.fontFamily ?? ResolveFontFamily(node) ?? "Arial",
                color = color,
                align = MapTextAlignHorizontal(node.textAlignHorizontal),
                alignVertical = MapTextAlignVertical(node.textAlignVertical),
                bold = weight >= 700,
                italic = false
            };
        }

        public static int ResolveFontWeight(FigmaNode node)
        {
            if (node.fontWeight > 0)
                return node.fontWeight;

            if (node.segments != null && node.segments.Count > 0)
                return node.segments[0].fontWeight > 0 ? node.segments[0].fontWeight : 400;

            return 400;
        }

        static float ResolveFontSize(FigmaNode node, float fallback)
        {
            if (node.fontSize > 0)
                return node.fontSize;
            if (node.segments != null && node.segments.Count > 0 && node.segments[0].fontSize > 0)
                return node.segments[0].fontSize;
            return fallback;
        }

        static string ResolveFontFamily(FigmaNode node)
        {
            if (!string.IsNullOrEmpty(node.fontFamily))
                return node.fontFamily;
            if (node.segments != null && node.segments.Count > 0)
                return node.segments[0].fontFamily;
            return null;
        }

        public static IRLayout MapLayout(FigmaLayout layout)
        {
            var result = new IRLayout();
            if (layout == null || layout.layoutMode == "NONE")
            {
                result.type = "none";
                return result;
            }

            result.type = layout.layoutMode == "VERTICAL" ? "vertical" : "horizontal";
            result.gap = layout.itemSpacing;
            result.paddingTop = layout.paddingTop;
            result.paddingRight = layout.paddingRight;
            result.paddingBottom = layout.paddingBottom;
            result.paddingLeft = layout.paddingLeft;
            result.meta = new Dictionary<string, object>
            {
                ["mainAlign"] = MapAxisAlign(layout.primaryAxisAlignItems),
                ["crossAlign"] = MapCrossAlign(layout.counterAxisAlignItems)
            };
            return result;
        }

        public static Dictionary<string, object> MapShadowMeta(FigmaNode node)
        {
            if (node.effects == null) return null;
            foreach (var effect in node.effects)
            {
                if (effect == null || effect.type != "DROP_SHADOW" || !effect.visible)
                    continue;
                return new Dictionary<string, object>
                {
                    ["x"] = effect.offset?.x ?? 0f,
                    ["y"] = effect.offset?.y ?? 0f,
                    ["blur"] = effect.radius,
                    ["color"] = effect.color
                };
            }
            return null;
        }

        static FigmaStroke GetFirstStroke(FigmaNode node)
        {
            if (node.strokes == null || node.strokes.Count == 0)
                return null;
            return node.strokes[0];
        }

        static string MapTextAlignHorizontal(string align)
        {
            if (align == "CENTER") return "center";
            if (align == "RIGHT") return "right";
            if (align == "JUSTIFIED") return "justified";
            return "left";
        }

        static string MapTextAlignVertical(string align)
        {
            if (align == "CENTER") return "center";
            if (align == "BOTTOM") return "bottom";
            return "top";
        }

        static string MapAxisAlign(string align)
        {
            if (string.IsNullOrEmpty(align)) return "start";
            return align switch
            {
                "CENTER" => "center",
                "MAX" => "end",
                "SPACE_BETWEEN" => "space-between",
                _ => "start"
            };
        }

        static string MapCrossAlign(string align)
        {
            if (string.IsNullOrEmpty(align)) return "cross-start";
            return align switch
            {
                "CENTER" => "cross-center",
                "MAX" => "cross-end",
                _ => "cross-start"
            };
        }

        public static string NormalizeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return "#000000";
            hex = hex.Trim();
            if (!hex.StartsWith("#"))
                hex = "#" + hex;
            if (hex.Length == 9)
                return hex.Substring(0, 7);
            return hex.ToLowerInvariant();
        }
    }
}
