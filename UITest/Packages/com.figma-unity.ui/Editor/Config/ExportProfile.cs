using System;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Config
{
    /// <summary>
    /// Controls which Unity Prefab changes are written back into Figma JSON.
    /// Mirrors Figma Tool 2 (Unity JSON importer) sync checkboxes.
    /// </summary>
    [Serializable]
    public class ExportProfile
    {
        public bool SyncTransform = true;
        public bool SyncVisibility = true;
        public bool SyncConstraints = true;
        public bool SyncTextContent = true;
        public bool SyncTextAlignment = true;
        public bool SyncTypography = false;
        public bool SyncFills = false;
        public bool SyncLayoutAdjustments = true;
        public bool PruneMissingNodes = true;

        public static ExportProfile DefaultUnityToFigma() => new ExportProfile();

        public static ExportProfile LayoutAndTextOnly() => new ExportProfile
        {
            SyncFills = false,
            SyncTypography = false,
            SyncLayoutAdjustments = true
        };

        public ExportProfile Clone()
        {
            return new ExportProfile
            {
                SyncTransform = SyncTransform,
                SyncVisibility = SyncVisibility,
                SyncConstraints = SyncConstraints,
                SyncTextContent = SyncTextContent,
                SyncTextAlignment = SyncTextAlignment,
                SyncTypography = SyncTypography,
                SyncFills = SyncFills,
                SyncLayoutAdjustments = SyncLayoutAdjustments,
                PruneMissingNodes = PruneMissingNodes
            };
        }

        public JObject ToMetadataJson()
        {
            return new JObject
            {
                ["syncTransform"] = SyncTransform,
                ["syncVisibility"] = SyncVisibility,
                ["syncConstraints"] = SyncConstraints,
                ["syncTextContent"] = SyncTextContent,
                ["syncTextAlignment"] = SyncTextAlignment,
                ["syncTypography"] = SyncTypography,
                ["syncFills"] = SyncFills,
                ["syncLayoutAdjustments"] = SyncLayoutAdjustments,
                ["pruneMissingNodes"] = PruneMissingNodes
            };
        }
    }
}
