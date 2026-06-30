using System;

namespace FigmaUnity.UI.Editor.Config
{
    public enum ImportRebuildMode
    {
        /// <summary>Delete prefab and rebuild entire tree from Figma.</summary>
        Replace = 0,
        /// <summary>Match nodes by irId; update layout/visuals; keep custom Unity components.</summary>
        Merge = 1
    }

    /// <summary>
    /// Controls which Figma JSON semantics are applied when building a Unity Prefab.
    /// Geometry and visuals (fills/strokes/corner radius) are always applied.
    /// </summary>
    [Serializable]
    public class ImportProfile
    {
        public ImportRebuildMode RebuildMode = ImportRebuildMode.Merge;
        public bool PruneMissingNodes = true;
        public bool ApplyConstraints = true;
        public bool ApplyAutoLayoutFill = true;
        public bool ApplyTypography = true;
        public bool ApplyTextAlignment = true;

        public static ImportProfile Full() => new ImportProfile();

        public static ImportProfile StaticAbsolute() => new ImportProfile
        {
            ApplyConstraints = false,
            ApplyAutoLayoutFill = false,
            ApplyTypography = false,
            ApplyTextAlignment = false
        };

        public ImportProfile Clone()
        {
            return new ImportProfile
            {
                RebuildMode = RebuildMode,
                PruneMissingNodes = PruneMissingNodes,
                ApplyConstraints = ApplyConstraints,
                ApplyAutoLayoutFill = ApplyAutoLayoutFill,
                ApplyTypography = ApplyTypography,
                ApplyTextAlignment = ApplyTextAlignment
            };
        }
    }
}
