using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FigmaUnity.UI.Editor.IR
{
    [Serializable]
    public class IRNode
    {
        public string version = "1.0.0";
        public string id;
        public string type;
        public float x;
        public float y;
        public float width;
        public float height;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public string anchor = "top-left";
        public List<IRFill> fills = new List<IRFill>();
        public IRStroke stroke;
        public IRPaintData paint;
        public float opacity = 1f;
        public float cornerRadius;
        public float rotation;
        public IRConstraints constraints;
        public string layoutSizingHorizontal;
        public string layoutSizingVertical;
        public IRLayout layout = new IRLayout();
        public IRText text;
        [JsonExtensionData]
        public Dictionary<string, object> meta = new Dictionary<string, object>();
        public List<IRNode> children = new List<IRNode>();
    }

    [Serializable]
    public class IRConstraints
    {
        public string horizontal = "MIN";
        public string vertical = "MIN";

        public static IRConstraints Default => new IRConstraints();
    }

    [Serializable]
    public class IRFill
    {
        public string type;
        public string color;
        public float opacity = 1f;
    }

    [Serializable]
    public class IRStroke
    {
        public string color;
        public float width;
        public float opacity = 1f;
    }

    [Serializable]
    public class IRText
    {
        public string content;
        public float fontSize;
        public string fontFamily;
        public string color;
        public string align;
        public string alignVertical;
        public bool bold;
        public bool italic;
        /// <summary>Figma textAutoResize: NONE | HEIGHT | WIDTH | WIDTH_AND_HEIGHT</summary>
        public string textAutoResize;
        public bool wordWrap = true;
        /// <summary>0 = unlimited.</summary>
        public int maxLines;
        /// <summary>TMP overflow: truncate | overflow | ellipsis</summary>
        public string overflow = "truncate";
        public float lineSpacing;
    }

    [Serializable]
    public class IRLayout
    {
        public string type = "none";
        public float gap;
        public float paddingTop;
        public float paddingRight;
        public float paddingBottom;
        public float paddingLeft;
        public string align;
        [JsonExtensionData]
        public Dictionary<string, object> meta = new Dictionary<string, object>();
    }
}
