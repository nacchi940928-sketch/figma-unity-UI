using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Converts Unity RectTransform geometry into Figma parent-relative x/y.
    /// Unity hierarchy may differ from Figma (e.g. home-button under root vs bottom-info-bar);
    /// always derive local coords from absolute rootX/rootY and the Figma template parent.
    /// </summary>
    public static class FigmaCoordExporter
    {
        public static void ResolveFigmaLocalCoords(
            UnityNodePatch patch,
            string nodeIrId,
            IReadOnlyDictionary<string, string> parentIrIdByChild,
            IReadOnlyDictionary<string, UnityNodePatch> patches,
            IReadOnlyDictionary<string, JObject> templateIndex,
            out float localX,
            out float localY)
        {
            var parentIrId = patch?.parentIrId;
            if (string.IsNullOrEmpty(parentIrId)
                && !string.IsNullOrEmpty(nodeIrId)
                && parentIrIdByChild != null)
            {
                parentIrIdByChild.TryGetValue(nodeIrId, out parentIrId);
            }

            ResolveParentRootCoords(parentIrId, patches, templateIndex, out var parentRootX, out var parentRootY);
            localX = patch.rootX - parentRootX;
            localY = patch.rootY - parentRootY;
        }

        public static void IndexTree(
            JObject node,
            string parentIrId,
            Dictionary<string, JObject> index,
            Dictionary<string, string> parentIrIdByChild)
        {
            if (node == null)
                return;

            var irId = node.Value<string>("irId");
            if (!string.IsNullOrEmpty(irId))
            {
                if (!index.ContainsKey(irId))
                    index[irId] = node;
                if (!string.IsNullOrEmpty(parentIrId))
                    parentIrIdByChild[irId] = parentIrId;
            }

            if (node["children"] is not JArray children)
                return;

            foreach (var childToken in children)
            {
                if (childToken is JObject child)
                    IndexTree(child, irId, index, parentIrIdByChild);
            }
        }

        static void ResolveParentRootCoords(
            string parentIrId,
            IReadOnlyDictionary<string, UnityNodePatch> patches,
            IReadOnlyDictionary<string, JObject> templateIndex,
            out float parentRootX,
            out float parentRootY)
        {
            parentRootX = 0f;
            parentRootY = 0f;
            if (string.IsNullOrEmpty(parentIrId))
                return;

            if (patches != null && patches.TryGetValue(parentIrId, out var parentPatch))
            {
                parentRootX = parentPatch.rootX;
                parentRootY = parentPatch.rootY;
                return;
            }

            if (templateIndex != null
                && templateIndex.TryGetValue(parentIrId, out var parentNode))
            {
                parentRootX = parentNode.Value<float?>("rootX") ?? parentNode.Value<float?>("x") ?? 0f;
                parentRootY = parentNode.Value<float?>("rootY") ?? parentNode.Value<float?>("y") ?? 0f;
            }
        }
    }
}
