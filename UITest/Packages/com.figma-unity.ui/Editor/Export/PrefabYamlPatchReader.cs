using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Reads IRBinding + RectTransform values directly from prefab YAML on disk.
    /// </summary>
    public static class PrefabYamlPatchReader
    {
        sealed class RectRecord
        {
            public long transformId;
            public long gameObjectId;
            public long fatherTransformId;
            public Vector2 anchoredPosition;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 sizeDelta;
            public float rotationZ;
        }

        sealed class BindingRecord
        {
            public long gameObjectId;
            public string irId;
            public string figmaNodeId;
        }

        public static Dictionary<string, UnityNodePatch> ExportFromAssetPath(string prefabAssetPath)
        {
            var absolutePath = ToAbsolutePath(prefabAssetPath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return null;

            return ExportFromYaml(File.ReadAllText(absolutePath));
        }

        public static Dictionary<string, UnityNodePatch> ExportFromYaml(string yaml)
        {
            var rects = ParseRectTransforms(yaml);
            var bindings = ParseBindings(yaml);
            if (bindings.Count == 0 || rects.Count == 0)
                return null;

            long rootGameObjectId = 0;
            foreach (var rect in rects.Values)
            {
                if (rect.fatherTransformId == 0)
                {
                    rootGameObjectId = rect.gameObjectId;
                    break;
                }
            }

            var rectByGameObject = new Dictionary<long, RectRecord>();
            foreach (var rect in rects.Values)
                rectByGameObject[rect.gameObjectId] = rect;

            var patches = new Dictionary<string, UnityNodePatch>();
            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.irId))
                    continue;
                if (!rectByGameObject.TryGetValue(binding.gameObjectId, out var rect))
                    continue;

                var data = ToData(rect);
                RectTransformSerializedReader.Data? parentData = null;
                if (binding.gameObjectId != rootGameObjectId
                    && rect.fatherTransformId != 0
                    && rects.TryGetValue(rect.fatherTransformId, out var parentRect))
                {
                    parentData = ToData(parentRect);
                }

                CoordReverseTranslator.ReadRect(data, parentData, out var x, out var y, out var width, out var height);
                ConstraintReverseTranslator.ReadConstraints(data, out var horizontal, out var vertical);

                patches[binding.irId] = new UnityNodePatch
                {
                    irId = binding.irId,
                    figmaNodeId = binding.figmaNodeId,
                    visible = true,
                    x = x,
                    y = y,
                    width = width,
                    height = height,
                    rotation = rect.rotationZ,
                    opacity = 1f,
                    constraintHorizontal = horizontal,
                    constraintVertical = vertical
                };
            }

            return patches.Count > 0 ? patches : null;
        }

        static RectTransformSerializedReader.Data ToData(RectRecord rect)
        {
            return new RectTransformSerializedReader.Data
            {
                anchorMin = rect.anchorMin,
                anchorMax = rect.anchorMax,
                pivot = rect.pivot,
                anchoredPosition = rect.anchoredPosition,
                sizeDelta = rect.sizeDelta
            };
        }

        static Dictionary<long, RectRecord> ParseRectTransforms(string yaml)
        {
            var result = new Dictionary<long, RectRecord>();
            foreach (var block in SplitBlocks(yaml))
            {
                if (!block.Contains("RectTransform:"))
                    continue;

                var transformId = ParseBlockId(block);
                if (transformId == 0)
                    continue;

                result[transformId] = new RectRecord
                {
                    transformId = transformId,
                    gameObjectId = ParseFileId(block, @"m_GameObject:\s*\{fileID:\s*(-?\d+)"),
                    fatherTransformId = ParseFileId(block, @"m_Father:\s*\{fileID:\s*(-?\d+)"),
                    anchoredPosition = ParseVector2(block, "m_AnchoredPosition"),
                    anchorMin = ParseVector2(block, "m_AnchorMin"),
                    anchorMax = ParseVector2(block, "m_AnchorMax"),
                    pivot = ParseVector2(block, "m_Pivot"),
                    sizeDelta = ParseVector2(block, "m_SizeDelta"),
                    rotationZ = ParseFloat(block, @"m_LocalEulerAnglesHint:\s*\{x:\s*[-\d.]+,\s*y:\s*[-\d.]+,\s*z:\s*([-\d.]+)")
                };
            }

            return result;
        }

        static List<BindingRecord> ParseBindings(string yaml)
        {
            var bindings = new List<BindingRecord>();
            foreach (var block in SplitBlocks(yaml))
            {
                if (!block.Contains("irId:"))
                    continue;

                var irId = ParseString(block, @"irId:\s*(\S+)");
                if (string.IsNullOrEmpty(irId))
                    continue;

                bindings.Add(new BindingRecord
                {
                    gameObjectId = ParseFileId(block, @"m_GameObject:\s*\{fileID:\s*(-?\d+)"),
                    irId = irId,
                    figmaNodeId = ParseString(block, @"figmaNodeId:\s*(\S+)")
                });
            }

            return bindings;
        }

        static IEnumerable<string> SplitBlocks(string yaml)
        {
            var parts = yaml.Split(new[] { "--- !u!" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                yield return part;
        }

        static long ParseBlockId(string block)
        {
            // Unity YAML: "224 &5374885596063157030\nRectTransform:" — use & fileID, not !u! type.
            var match = Regex.Match(block.TrimStart(), @"^\d+\s*&(\d+)");
            return match.Success
                ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
        }

        static long ParseFileId(string block, string pattern)
        {
            var match = Regex.Match(block, pattern);
            return match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        static Vector2 ParseVector2(string block, string fieldName)
        {
            var match = Regex.Match(
                block,
                fieldName + @"\s*:\s*\{x:\s*([-\d.]+),\s*y:\s*([-\d.]+)\}");
            if (!match.Success)
                return Vector2.zero;

            return new Vector2(
                float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        static float ParseFloat(string block, string pattern)
        {
            var match = Regex.Match(block, pattern);
            return match.Success
                ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0f;
        }

        static string ParseString(string block, string pattern)
        {
            var match = Regex.Match(block, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        public static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (Path.IsPathRooted(assetPath))
                return assetPath.Replace('\\', '/');

            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace('\\', '/');
            }

            return Path.GetFullPath(assetPath).Replace('\\', '/');
        }
    }
}
