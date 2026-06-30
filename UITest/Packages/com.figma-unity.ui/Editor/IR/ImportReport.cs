using System.Collections.Generic;

namespace FigmaUnity.UI.Editor.IR
{
    public class ImportReport
    {
        public int NodeCount;
        public int TextCount;
        public int ImageCount;
        public int VectorPlaceholderCount;
        public int LineCount;
        public int LayoutFillGroupCount;
        public int ShadowSkippedCount;
        public int MergeUpdatedCount;
        public int MergeCreatedCount;
        public int MergeRemovedCount;
        public int MergePreservedUnityChildren;
        public int MergePreservedAnchorsCount;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> MissingAssets = new List<string>();

        public void LogSummary()
        {
            var mergePart = MergeUpdatedCount > 0 || MergeCreatedCount > 0 || MergeRemovedCount > 0
                ? $" merge(updated={MergeUpdatedCount}, created={MergeCreatedCount}, removed={MergeRemovedCount}, " +
                  $"unityChildrenKept={MergePreservedUnityChildren}, anchorsPreserved={MergePreservedAnchorsCount})"
                : string.Empty;

            UnityEngine.Debug.Log(
                $"[Figma UI Import] nodes={NodeCount} text={TextCount} image={ImageCount} " +
                $"vectorPlaceholders={VectorPlaceholderCount} lines={LineCount} " +
                $"layoutFillGroups={LayoutFillGroupCount} shadowsSkipped={ShadowSkippedCount}{mergePart} " +
                $"warnings={Warnings.Count} missingAssets={MissingAssets.Count}");
            foreach (var w in Warnings)
                UnityEngine.Debug.LogWarning($"[Figma UI Import] {w}");
            foreach (var m in MissingAssets)
                UnityEngine.Debug.LogWarning($"[Figma UI Import] Missing asset: {m}");
        }
    }
}
