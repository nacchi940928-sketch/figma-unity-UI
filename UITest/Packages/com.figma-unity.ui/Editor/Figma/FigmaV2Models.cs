using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FigmaUnity.UI.Editor.Figma
{
    [Serializable]
    public class FigmaExportDocument
    {
        public FigmaMetadata metadata;
        public FigmaNode node;
    }

    [Serializable]
    public class FigmaMetadata
    {
        public string exportedAt;
        public string componentName;
        public string rootIrId;
        public int totalElements;
        public bool rootNormalized;
        public string plugin;
    }

    [Serializable]
    public class FigmaNode
    {
        public string id;
        public string irId;
        public string name;
        public string type;
        public bool visible = true;
        public float x;
        public float y;
        public float rootX;
        public float rootY;
        public float width;
        public float height;
        public float rotation;
        public float opacity = 1f;
        public List<FigmaFill> fills = new List<FigmaFill>();
        public List<FigmaStroke> strokes = new List<FigmaStroke>();
        public float strokeWeight;
        public float cornerRadius;
        public List<FigmaEffect> effects = new List<FigmaEffect>();
        public FigmaConstraints constraints;
        public string layoutSizingHorizontal;
        public string layoutSizingVertical;
        public FigmaLayout layout;
        public string characters;
        public string fontFamily;
        public float fontSize;
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int fontWeight;
        public string textAlignHorizontal;
        public string textAlignVertical;
        public List<FigmaTextSegment> segments = new List<FigmaTextSegment>();
        public List<FigmaNode> children = new List<FigmaNode>();
    }

    [Serializable]
    public class FigmaTextSegment
    {
        public string text;
        public string fontFamily;
        public string fontStyle;
        public float fontSize;
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int fontWeight;
        public string color;
    }

    [Serializable]
    public class FigmaFill
    {
        public string type;
        public string color;
        public float opacity = 1f;
        public string imageHash;
        public string imageFile;
        public string scaleMode;
    }

    [Serializable]
    public class FigmaStroke
    {
        public string color;
        public float opacity = 1f;
    }

    [Serializable]
    public class FigmaEffect
    {
        public string type;
        public string color;
        public FigmaOffset offset;
        public float radius;
        public float spread;
        public bool visible;
    }

    [Serializable]
    public class FigmaOffset
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class FigmaConstraints
    {
        public string horizontal;
        public string vertical;
    }

    [Serializable]
    public class FigmaLayout
    {
        public string layoutMode;
        public string primaryAxisAlignItems;
        public string counterAxisAlignItems;
        public float paddingTop;
        public float paddingRight;
        public float paddingBottom;
        public float paddingLeft;
        public float itemSpacing;
    }
}
