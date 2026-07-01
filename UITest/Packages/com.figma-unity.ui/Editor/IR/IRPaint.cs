using System;
using System.Collections.Generic;

namespace FigmaUnity.UI.Editor.IR
{
    [Serializable]
    public class IRPaintData
    {
        public List<IRPaintFill> fills = new List<IRPaintFill>();
        public IRStroke stroke;
        public string strokeAlign = "INSIDE";
        public List<IREffect> effects = new List<IREffect>();
    }

    [Serializable]
    public class IRPaintFill
    {
        public string type;
        public string color;
        public float opacity = 1f;
        public string imageFile;
        public IRGradientTransform transform;
        public List<IRGradientStop> stops = new List<IRGradientStop>();
    }

    [Serializable]
    public class IRGradientTransform
    {
        public float a = 1f;
        public float b;
        public float c;
        public float d = 1f;
        public float tx;
        public float ty;
    }

    [Serializable]
    public class IRGradientStop
    {
        public float position;
        public string color;
    }

    [Serializable]
    public class IREffect
    {
        public string type;
        public bool visible = true;
        public string color;
        public float offsetX;
        public float offsetY;
        public float radius;
        public float spread;
    }
}
