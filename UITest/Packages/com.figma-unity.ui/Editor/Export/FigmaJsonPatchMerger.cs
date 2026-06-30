using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FigmaUnity.UI.Editor.Figma;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Patches Unity changes into the original Figma export JSON without re-serializing
    /// through C# models (preserves blendMode, strokeAlign, layout sizing fields, etc.).
    /// </summary>
    public static class FigmaJsonPatchMerger
    {
        public static FigmaDocumentMerger.MergeResult MergeFile(
            string templatePath,
            Dictionary<string, UnityNodePatch> patches,
            string outputPath,
            FigmaDocumentMerger.MergeOptions options = null)
        {
            options ??= new FigmaDocumentMerger.MergeOptions();
            var root = FigmaDocumentSerializer.Load(templatePath);

            var result = new FigmaDocumentMerger.MergeResult();
            var matched = new HashSet<string>();

            var node = root["node"] as JObject;
            if (node == null)
                throw new ArgumentException("Template JSON has no root node.");

            var rootIrId = node.Value<string>("irId");
            if (string.IsNullOrEmpty(rootIrId) || !patches.ContainsKey(rootIrId))
                throw new ArgumentException("Prefab root is missing IRBinding or does not match template rootIrId.");

            result.LayoutChangedCount = 0;
            result.ConstraintsChangedCount = 0;

            var index = IndexNodesByIrId(node);
            ApplyPatches(index, patches, matched, result, options);
            result.UnityNodeCount = patches.Count;

            AutoLayoutExportPatcher.NormalizeResult layoutNormalize = null;
            if (options.ExportProfile.SyncLayoutAdjustments)
            {
                layoutNormalize = AutoLayoutExportPatcher.NormalizeTree(node);
                result.AutoLayoutBrokenCount = layoutNormalize.BrokenCount;
            }

            if (options.ExportProfile.PruneMissingNodes)
            {
                var allowedIrIds = new HashSet<string>(patches.Keys, StringComparer.Ordinal);
                result.PrunedIrIds.Clear();
                result.RemovedCount = 0;

                var filtered = FilterNodeTreeToPrefab(node, allowedIrIds, result);
                if (filtered == null)
                    throw new InvalidOperationException("Prefab root was removed during prune.");

                root["node"] = filtered;
                node = filtered;

                var orphans = new List<string>();
                CollectDisallowedIrIds(node, allowedIrIds, orphans);
                if (orphans.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Prune failed to remove Prefab-deleted nodes: " + string.Join(", ", orphans));
                }
            }

            result.TotalElements = RecomputeRootCoords(node, 0f, 0f);

            if (options.IncludeLayout
                && root["metadata"]?.Value<bool?>("rootNormalized") == true)
            {
                node["x"] = 0;
                node["y"] = 0;
                node["rootX"] = 0;
                node["rootY"] = 0;
            }

            foreach (var irId in patches.Keys)
            {
                if (!matched.Contains(irId))
                    result.Warnings.Add($"Unity node not found in template JSON: {irId}");
            }

            if (root["metadata"] is JObject metadata)
            {
                metadata["exportedAt"] = DateTime.UtcNow.ToString("o");
                metadata["totalElements"] = result.TotalElements;
                metadata["unityNodeCount"] = result.UnityNodeCount;
                metadata["removedNodeCount"] = result.RemovedCount;
                metadata["preserveTypography"] = options.PreserveTypography;
                metadata["exportProfile"] = options.ExportProfile.ToMetadataJson();
                metadata["sourceFormat"] = FigmaDocumentSerializer.IsXmlPath(templatePath) ? "xml" : "json";
                metadata["outputFormat"] = FigmaDocumentSerializer.IsXmlPath(outputPath) ? "xml" : "json";
                if (result.PrunedIrIds.Count > 0)
                    metadata["prunedIrIds"] = new JArray(result.PrunedIrIds);
                metadata["coordinateConvention"] = new JObject
                {
                    ["version"] = "1",
                    ["jsonOrigin"] = "parent-top-left",
                    ["yAxis"] = "down",
                    ["figmaRootAnchor"] = "top-left",
                    ["unityRootAnchor"] = "center",
                    ["rootNormalized"] = root["metadata"]?.Value<bool?>("rootNormalized") ?? true,
                    ["note"] = "JSON x/y are Figma semantics. Unity root uses center anchor; child coords are relative to parent top-left."
                };

                if (layoutNormalize != null)
                {
                    metadata["layoutAdjustments"] = new JObject
                    {
                        ["brokenAutoLayoutCount"] = layoutNormalize.BrokenCount,
                        ["brokenAutoLayoutIrIds"] = new JArray(layoutNormalize.BrokenIrIds),
                        ["note"] = "Unity baked x/y no longer matches template Auto Layout; layoutMode set to NONE for these nodes."
                    };
                }
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (root["metadata"] is JObject metadataForAssets)
            {
                metadataForAssets["assetDir"] = Path.GetFullPath(dir).Replace('\\', '/');
                var fullOutput = Path.GetFullPath(outputPath).Replace('\\', '/');
                metadataForAssets["documentPath"] = fullOutput;
                if (FigmaDocumentSerializer.IsXmlPath(outputPath))
                    metadataForAssets["xmlPath"] = fullOutput;
                else
                    metadataForAssets["jsonPath"] = fullOutput;
            }

            FigmaDocumentSerializer.Save(root, outputPath);

            if (options.ExportProfile.PruneMissingNodes)
            {
                if (result.MissingInUnityCount > 0 && result.RemovedCount == 0)
                {
                    result.Warnings.Add(
                        $"Template has {result.MissingInUnityCount} nodes not in Prefab but prune removed 0. " +
                        "Save the Prefab and re-export.");
                }

                foreach (var irId in result.PrunedIrIds)
                {
                    if (ContainsIrId(node, irId))
                    {
                        result.Warnings.Add(
                            $"Prune integrity check failed: {irId} is still in the output JSON tree.");
                    }
                }
            }

            return result;
        }

        static Dictionary<string, JObject> IndexNodesByIrId(JObject node)
        {
            var index = new Dictionary<string, JObject>();
            IndexNode(node, index);
            return index;
        }

        static void IndexNode(JObject node, Dictionary<string, JObject> index)
        {
            if (node == null)
                return;

            var irId = node.Value<string>("irId");
            if (!string.IsNullOrEmpty(irId) && !index.ContainsKey(irId))
                index[irId] = node;

            if (node["children"] is not JArray children)
                return;

            foreach (var childToken in children)
            {
                if (childToken is JObject child)
                    IndexNode(child, index);
            }
        }

        static void ApplyPatches(
            Dictionary<string, JObject> index,
            Dictionary<string, UnityNodePatch> patches,
            HashSet<string> matched,
            FigmaDocumentMerger.MergeResult result,
            FigmaDocumentMerger.MergeOptions options)
        {
            foreach (var pair in patches)
            {
                if (!index.TryGetValue(pair.Key, out var node))
                    continue;

                ApplyPatch(node, pair.Value, options, result);
                matched.Add(pair.Key);
                result.UpdatedCount++;
            }

            foreach (var pair in index)
            {
                if (!patches.ContainsKey(pair.Key))
                    result.MissingInUnityCount++;
            }
        }

        static int RecomputeRootCoords(JObject node, float parentRootX, float parentRootY)
        {
            var x = node.Value<float?>("x") ?? 0f;
            var y = node.Value<float?>("y") ?? 0f;
            var rootX = parentRootX + x;
            var rootY = parentRootY + y;

            node["rootX"] = rootX;
            node["rootY"] = rootY;

            var count = 1;
            if (node["children"] is not JArray children)
                return count;

            foreach (var childToken in children)
            {
                if (childToken is JObject child)
                    count += RecomputeRootCoords(child, rootX, rootY);
            }

            return count;
        }

        /// <summary>
        /// Rebuilds the subtree, keeping only nodes that still exist on the Prefab (irId in patches).
        /// Uses DeepClone so child-array replacement is reliable across Newtonsoft versions.
        /// </summary>
        static JObject FilterNodeTreeToPrefab(
            JObject source,
            HashSet<string> allowedIrIds,
            FigmaDocumentMerger.MergeResult result)
        {
            var irId = source.Value<string>("irId");
            if (!string.IsNullOrEmpty(irId) && !allowedIrIds.Contains(irId))
            {
                if (!result.PrunedIrIds.Contains(irId))
                    result.PrunedIrIds.Add(irId);
                result.RemovedCount += CountSubtree(source);
                return null;
            }

            var clone = (JObject)source.DeepClone();
            if (clone["children"] is not JArray children || children.Count == 0)
                return clone;

            var kept = new JArray();
            foreach (var childToken in children)
            {
                if (childToken is not JObject child)
                    continue;

                var filteredChild = FilterNodeTreeToPrefab(child, allowedIrIds, result);
                if (filteredChild != null)
                    kept.Add(filteredChild);
            }

            clone["children"] = kept;
            return clone;
        }

        static void CollectDisallowedIrIds(JObject node, HashSet<string> allowedIrIds, List<string> found)
        {
            var irId = node.Value<string>("irId");
            if (!string.IsNullOrEmpty(irId) && !allowedIrIds.Contains(irId) && !found.Contains(irId))
                found.Add(irId);

            if (node["children"] is not JArray children)
                return;

            foreach (var childToken in children)
            {
                if (childToken is JObject child)
                    CollectDisallowedIrIds(child, allowedIrIds, found);
            }
        }

        static bool ContainsIrId(JObject node, string irId)
        {
            if (string.Equals(node.Value<string>("irId"), irId, StringComparison.Ordinal))
                return true;

            if (node["children"] is not JArray children)
                return false;

            foreach (var childToken in children)
            {
                if (childToken is JObject child && ContainsIrId(child, irId))
                    return true;
            }

            return false;
        }

        static int CountSubtree(JToken token)
        {
            if (token is not JObject node)
                return 0;

            var count = 1;
            if (node["children"] is not JArray children)
                return count;

            foreach (var child in children)
                count += CountSubtree(child);

            return count;
        }

        static void ApplyPatch(
            JObject node,
            UnityNodePatch patch,
            FigmaDocumentMerger.MergeOptions options,
            FigmaDocumentMerger.MergeResult result)
        {
            var profile = options.ExportProfile;

            if (profile.SyncVisibility)
            {
                node["visible"] = patch.visible;
                node["opacity"] = patch.opacity;
            }

            if (profile.SyncTransform)
            {
                if (LayoutDiffers(node, patch))
                    result.LayoutChangedCount++;

                node["x"] = patch.x;
                node["y"] = patch.y;
                node["width"] = patch.width;
                node["height"] = patch.height;
                node["rotation"] = patch.rotation;
            }

            if (profile.SyncConstraints)
            {
                if (ConstraintsDiffer(node, patch))
                    result.ConstraintsChangedCount++;

                node["constraints"] = new JObject
                {
                    ["horizontal"] = patch.constraintHorizontal,
                    ["vertical"] = patch.constraintVertical
                };
            }

            if (profile.SyncFills && patch.fills != null && patch.fills.Count > 0)
            {
                var fills = new JArray();
                foreach (var fill in patch.fills)
                {
                    fills.Add(new JObject
                    {
                        ["type"] = "SOLID",
                        ["color"] = fill.color,
                        ["opacity"] = fill.opacity
                    });
                }
                node["fills"] = fills;
            }

            ApplyTextPatch(node, patch, options);
        }

        static void ApplyTextPatch(
            JObject node,
            UnityNodePatch patch,
            FigmaDocumentMerger.MergeOptions options)
        {
            if (patch.text == null || string.IsNullOrEmpty(patch.text.content))
                return;

            var profile = options.ExportProfile;

            if (profile.SyncTextContent)
            {
                node["characters"] = patch.text.content;

                if (node["segments"] is JArray segments && segments.Count > 0 && segments[0] is JObject segment)
                    segment["text"] = patch.text.content;
            }

            if (profile.SyncTextAlignment)
                ApplyTextAlignment(node, patch);

            if (!profile.SyncTypography)
                return;

            node["fontFamily"] = patch.text.fontFamily;
            node["fontSize"] = patch.text.fontSize;
            node["fontWeight"] = patch.text.bold ? 700 : 400;

            if (node["segments"] is JArray preservedSegments
                && preservedSegments.Count > 0
                && preservedSegments[0] is JObject preservedSegment)
            {
                preservedSegment["fontFamily"] = patch.text.fontFamily;
                preservedSegment["fontSize"] = patch.text.fontSize;
                preservedSegment["fontWeight"] = patch.text.bold ? 700 : 400;
                preservedSegment["color"] = patch.text.color;
            }
            else
            {
                node["segments"] = new JArray
                {
                    new JObject
                    {
                        ["text"] = patch.text.content,
                        ["fontFamily"] = patch.text.fontFamily,
                        ["fontSize"] = patch.text.fontSize,
                        ["fontWeight"] = patch.text.bold ? 700 : 400,
                        ["color"] = patch.text.color
                    }
                };
            }

            if (patch.fills == null || patch.fills.Count == 0)
            {
                node["fills"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "SOLID",
                        ["color"] = patch.text.color,
                        ["opacity"] = 1f
                    }
                };
            }
        }

        static void ApplyTextAlignment(JObject node, UnityNodePatch patch)
        {
            if (patch.text == null)
                return;

            if (!string.IsNullOrEmpty(patch.text.align))
                node["textAlignHorizontal"] = patch.text.align.ToUpperInvariant();

            if (!string.IsNullOrEmpty(patch.text.alignVertical))
                node["textAlignVertical"] = patch.text.alignVertical.ToUpperInvariant();
        }

        static bool LayoutDiffers(JObject node, UnityNodePatch patch)
        {
            return !NearlyEqual(node.Value<float?>("x"), patch.x)
                || !NearlyEqual(node.Value<float?>("y"), patch.y)
                || !NearlyEqual(node.Value<float?>("width"), patch.width)
                || !NearlyEqual(node.Value<float?>("height"), patch.height)
                || !NearlyEqual(node.Value<float?>("rotation"), patch.rotation);
        }

        static bool ConstraintsDiffer(JObject node, UnityNodePatch patch)
        {
            var constraints = node["constraints"] as JObject;
            var horizontal = constraints?.Value<string>("horizontal");
            var vertical = constraints?.Value<string>("vertical");

            return !string.Equals(horizontal, patch.constraintHorizontal, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(vertical, patch.constraintVertical, StringComparison.OrdinalIgnoreCase);
        }

        static bool NearlyEqual(float? a, float b, float epsilon = 0.01f)
        {
            return Math.Abs((a ?? 0f) - b) <= epsilon;
        }
    }
}
