using System.Collections.Generic;
using System.Text;
using FigmaUnity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUnity.UI.Editor.Export
{
    public sealed class UnityNodeBindingInfo
    {
        public string objectName;
        public string hierarchyPath;
        public string irId;
        public string parentName;
        public string parentIrId;
    }

    /// <summary>
    /// Finds Unity-only UI nodes and adds IRBinding so they can be exported to Figma.
    /// </summary>
    public static class UnityNodeBindingCompleter
    {
        internal const string UtilityPaintChildName = "_figmaPaint";

        public static List<UnityNodeBindingInfo> FindPendingNodes(GameObject prefabRoot)
        {
            var pending = new List<UnityNodeBindingInfo>();
            if (prefabRoot == null)
                return pending;

            foreach (var rt in EnumeratePendingTransforms(prefabRoot))
            {
                var parentIrId = FindParentIrId(rt.transform, prefabRoot);
                if (string.IsNullOrEmpty(parentIrId))
                    continue;

                pending.Add(new UnityNodeBindingInfo
                {
                    objectName = rt.gameObject.name,
                    hierarchyPath = BuildHierarchyPath(rt.transform, prefabRoot.transform),
                    parentName = rt.transform.parent != null ? rt.transform.parent.name : prefabRoot.name,
                    parentIrId = parentIrId
                });
            }

            return pending;
        }

        public static List<UnityNodeBindingInfo> CompleteBindings(GameObject prefabRoot)
        {
            var completed = new List<UnityNodeBindingInfo>();
            if (prefabRoot == null)
                return completed;

            var usedIrIds = CollectExistingIrIds(prefabRoot);
            var candidates = EnumeratePendingTransforms(prefabRoot);

            foreach (var rt in candidates)
            {
                var parentIrId = FindParentIrId(rt.transform, prefabRoot);
                if (string.IsNullOrEmpty(parentIrId))
                    continue;

                var binding = rt.GetComponent<IRBinding>();
                if (binding == null)
                    binding = rt.gameObject.AddComponent<IRBinding>();

                if (!string.IsNullOrEmpty(binding.irId))
                    continue;

                var irId = GenerateUnityIrId(rt.gameObject.name, usedIrIds);
                usedIrIds.Add(irId);
                binding.irId = irId;
                binding.figmaNodeId = string.Empty;

                completed.Add(new UnityNodeBindingInfo
                {
                    objectName = rt.gameObject.name,
                    hierarchyPath = BuildHierarchyPath(rt.transform, prefabRoot.transform),
                    irId = irId,
                    parentName = rt.transform.parent != null ? rt.transform.parent.name : prefabRoot.name,
                    parentIrId = parentIrId
                });
            }

            return completed;
        }

        public static bool IsUnityAddedBinding(IRBinding binding)
        {
            return binding != null
                && !string.IsNullOrEmpty(binding.irId)
                && string.IsNullOrEmpty(binding.figmaNodeId);
        }

        public static string FindParentIrId(Transform node, GameObject prefabRoot)
        {
            var current = node.parent;
            while (current != null)
            {
                if (current.gameObject == prefabRoot)
                    return prefabRoot.GetComponent<IRBinding>()?.irId;

                var binding = current.GetComponent<IRBinding>();
                if (binding != null && !string.IsNullOrEmpty(binding.irId))
                    return binding.irId;

                current = current.parent;
            }

            return null;
        }

        public static int GetHierarchyDepth(Transform node, Transform prefabRoot)
        {
            var depth = 0;
            var current = node;
            while (current != null && current != prefabRoot)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        public static string GenerateUnityIrId(string nodeName, HashSet<string> usedIrIds)
        {
            var slug = SlugifyNodeName(nodeName);
            var baseId = slug + "__unity";
            if (!usedIrIds.Contains(baseId))
                return baseId;

            for (var i = 2; i < 1000; i++)
            {
                var candidate = baseId + "_" + i;
                if (!usedIrIds.Contains(candidate))
                    return candidate;
            }

            return baseId + "_" + nodeName.GetHashCode().ToString("x8");
        }

        public static string SlugifyNodeName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return "node";

            var slug = new StringBuilder(nodeName.Length);
            foreach (var c in nodeName.Trim())
            {
                if (char.IsLetterOrDigit(c))
                    slug.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '_' || c == '-')
                    slug.Append('-');
            }

            return slug.Length > 0 ? slug.ToString() : "node";
        }

        public static string FormatBindingSummary(IReadOnlyList<UnityNodeBindingInfo> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return "没有需要补全的节点。";

            var lines = new StringBuilder();
            lines.AppendLine($"共 {nodes.Count} 个节点：");
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                lines.Append(i + 1).Append(". ").Append(node.objectName);
                if (!string.IsNullOrEmpty(node.irId))
                    lines.Append("  →  irId: ").Append(node.irId);
                lines.Append("  （父级: ").Append(node.parentName).Append(')');
                if (i < nodes.Count - 1)
                    lines.AppendLine();
            }

            return lines.ToString();
        }

        static IEnumerable<RectTransform> EnumeratePendingTransforms(GameObject prefabRoot)
        {
            var candidates = new List<(RectTransform rt, int depth)>();

            foreach (var rt in prefabRoot.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.gameObject == prefabRoot)
                    continue;
                if (IsSkippedExportNode(rt.gameObject))
                    continue;
                if (!HasExportableContent(rt.gameObject))
                    continue;

                var binding = rt.GetComponent<IRBinding>();
                if (binding != null && !string.IsNullOrEmpty(binding.irId))
                    continue;

                candidates.Add((rt, GetHierarchyDepth(rt.transform, prefabRoot.transform)));
            }

            candidates.Sort((a, b) => a.depth.CompareTo(b.depth));
            foreach (var (rt, _) in candidates)
                yield return rt;
        }

        static HashSet<string> CollectExistingIrIds(GameObject prefabRoot)
        {
            var usedIrIds = new HashSet<string>();
            foreach (var binding in prefabRoot.GetComponentsInChildren<IRBinding>(true))
            {
                if (binding != null && !string.IsNullOrEmpty(binding.irId))
                    usedIrIds.Add(binding.irId);
            }

            return usedIrIds;
        }

        static bool IsSkippedExportNode(GameObject go)
        {
            var current = go.transform;
            while (current != null)
            {
                if (current.name == UtilityPaintChildName)
                    return true;
                current = current.parent;
            }

            return false;
        }

        static bool HasExportableContent(GameObject go)
        {
            if (go.TryGetComponent<TextMeshProUGUI>(out _))
                return true;
            if (go.TryGetComponent<RawImage>(out var raw) && raw.texture != null)
                return true;
            if (go.TryGetComponent<Image>(out var image) && (image.sprite != null || image.color.a > 0.01f))
                return true;

            return false;
        }

        static string BuildHierarchyPath(Transform node, Transform prefabRoot)
        {
            var segments = new List<string>();
            var current = node;
            while (current != null && current != prefabRoot)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
