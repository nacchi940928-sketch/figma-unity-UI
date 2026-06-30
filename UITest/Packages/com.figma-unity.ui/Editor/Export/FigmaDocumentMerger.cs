using System;
using System.Collections.Generic;
using FigmaUnity.UI.Editor.Config;
using FigmaUnity.UI.Editor.Figma;
using FigmaUnity.UI.Editor.IR;

namespace FigmaUnity.UI.Editor.Export
{
    public static class FigmaDocumentMerger
    {
        public class MergeOptions
        {
            public ExportProfile ExportProfile = ExportProfile.DefaultUnityToFigma();

            public bool IncludeLayout
            {
                get => ExportProfile.SyncTransform;
                set => ExportProfile.SyncTransform = value;
            }

            public bool PruneMissingNodes
            {
                get => ExportProfile.PruneMissingNodes;
                set => ExportProfile.PruneMissingNodes = value;
            }

            public bool PreserveTypography
            {
                get => !ExportProfile.SyncTypography;
                set => ExportProfile.SyncTypography = !value;
            }
        }

        public class MergeResult
        {
            public FigmaExportDocument Document;
            public int UpdatedCount;
            public int LayoutChangedCount;
            public int ConstraintsChangedCount;
            public int RemovedCount;
            public int MissingInUnityCount;
            public int TotalElements;
            public int UnityNodeCount;
            public int AutoLayoutBrokenCount;
            public List<string> Warnings = new List<string>();
            public List<string> PrunedIrIds = new List<string>();
        }

        public static MergeResult Merge(
            FigmaExportDocument template,
            Dictionary<string, UnityNodePatch> patches,
            MergeOptions options = null)
        {
            options ??= new MergeOptions();
            if (template?.node == null)
                throw new ArgumentException("Template document has no root node.");

            var profile = options.ExportProfile;
            var result = new MergeResult { Document = template };

            var matched = new HashSet<string>();
            MergeNode(template.node, patches, matched, result, profile);

            foreach (var irId in patches.Keys)
            {
                if (!matched.Contains(irId))
                    result.Warnings.Add($"Unity node not found in template JSON: {irId}");
            }

            if (profile.SyncTransform)
                RecomputeRootCoords(template.node, 0f, 0f);

            if (template.metadata != null && template.metadata.rootNormalized && profile.SyncTransform)
            {
                template.node.x = 0f;
                template.node.y = 0f;
                template.node.rootX = 0f;
                template.node.rootY = 0f;
            }

            if (template.metadata == null)
                template.metadata = new FigmaMetadata();

            template.metadata.exportedAt = DateTime.UtcNow.ToString("o");
            template.metadata.plugin = "Unity IRExporter v2";

            return result;
        }

        static void MergeNode(
            FigmaNode node,
            Dictionary<string, UnityNodePatch> patches,
            HashSet<string> matched,
            MergeResult result,
            ExportProfile profile)
        {
            if (node == null)
                return;

            if (!string.IsNullOrEmpty(node.irId) && patches.TryGetValue(node.irId, out var patch))
            {
                ApplyPatch(node, patch, profile);
                matched.Add(node.irId);
                result.UpdatedCount++;
            }
            else if (!string.IsNullOrEmpty(node.irId))
            {
                result.MissingInUnityCount++;
            }

            if (node.children == null)
                return;

            foreach (var child in node.children)
                MergeNode(child, patches, matched, result, profile);
        }

        static void ApplyPatch(FigmaNode node, UnityNodePatch patch, ExportProfile profile)
        {
            if (profile.SyncVisibility)
            {
                node.visible = patch.visible;
                node.opacity = patch.opacity;
            }

            if (profile.SyncTransform)
            {
                node.x = patch.x;
                node.y = patch.y;
                node.width = patch.width;
                node.height = patch.height;
                node.rotation = patch.rotation;
            }

            if (profile.SyncConstraints)
            {
                node.constraints ??= new FigmaConstraints();
                node.constraints.horizontal = patch.constraintHorizontal;
                node.constraints.vertical = patch.constraintVertical;
            }

            if (profile.SyncFills && patch.fills != null && patch.fills.Count > 0)
            {
                node.fills = new List<FigmaFill>();
                foreach (var fill in patch.fills)
                {
                    node.fills.Add(new FigmaFill
                    {
                        type = "SOLID",
                        color = fill.color,
                        opacity = fill.opacity
                    });
                }
            }

            if (patch.text != null)
                ApplyTextPatch(node, patch, profile);
        }

        static void ApplyTextPatch(FigmaNode node, UnityNodePatch patch, ExportProfile profile)
        {
            if (profile.SyncTextContent)
            {
                node.characters = patch.text.content;
                if (node.segments != null && node.segments.Count > 0)
                    node.segments[0].text = patch.text.content;
            }

            if (profile.SyncTextAlignment)
            {
                if (!string.IsNullOrEmpty(patch.text.align))
                    node.textAlignHorizontal = patch.text.align.ToUpperInvariant();
                if (!string.IsNullOrEmpty(patch.text.alignVertical))
                    node.textAlignVertical = patch.text.alignVertical.ToUpperInvariant();
            }

            if (!profile.SyncTypography)
                return;

            node.fontFamily = patch.text.fontFamily;
            node.fontSize = patch.text.fontSize;
            node.fontWeight = patch.text.bold ? 700 : 400;

            if (node.segments == null || node.segments.Count == 0)
            {
                node.segments = new List<FigmaTextSegment>
                {
                    new FigmaTextSegment
                    {
                        text = patch.text.content,
                        fontFamily = patch.text.fontFamily,
                        fontSize = patch.text.fontSize,
                        fontWeight = patch.text.bold ? 700 : 400,
                        color = patch.text.color
                    }
                };
            }
            else
            {
                node.segments[0].fontFamily = patch.text.fontFamily;
                node.segments[0].fontSize = patch.text.fontSize;
                node.segments[0].fontWeight = patch.text.bold ? 700 : 400;
                node.segments[0].color = patch.text.color;
            }

            if (node.fills == null || node.fills.Count == 0)
            {
                node.fills = new List<FigmaFill>
                {
                    new FigmaFill
                    {
                        type = "SOLID",
                        color = patch.text.color,
                        opacity = 1f
                    }
                };
            }
        }

        static void RecomputeRootCoords(FigmaNode node, float parentRootX, float parentRootY)
        {
            node.rootX = parentRootX + node.x;
            node.rootY = parentRootY + node.y;

            if (node.children == null)
                return;

            foreach (var child in node.children)
                RecomputeRootCoords(child, node.rootX, node.rootY);
        }

        static int CountNodes(FigmaNode node)
        {
            if (node == null)
                return 0;

            var count = 1;
            if (node.children == null)
                return count;

            foreach (var child in node.children)
                count += CountNodes(child);
            return count;
        }
    }
}
