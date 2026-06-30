using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    public static class ExportHierarchyUtil
    {
        public sealed class ExportScope : IDisposable
        {
            public GameObject Root { get; }
            readonly Action _cleanup;

            ExportScope(GameObject root, Action cleanup)
            {
                Root = root;
                _cleanup = cleanup;
            }

            public void Dispose()
            {
                _cleanup?.Invoke();
            }

            public static ExportScope Create(GameObject prefab)
            {
                if (prefab == null)
                    return new ExportScope(null, null);

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage?.prefabContentsRoot != null)
                    return BuildScope(stage.prefabContentsRoot, null);

                var assetPath = ResolvePrefabAssetPath(prefab);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var contents = PrefabUtility.LoadPrefabContents(assetPath);
                    return BuildScope(contents, () => PrefabUtility.UnloadPrefabContents(contents));
                }

                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(prefab) ?? prefab;
                return BuildScope(instanceRoot, null);
            }

            public static string ResolvePrefabAssetPath(GameObject go)
            {
                if (go == null)
                    return null;

                var path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return path;

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && !string.IsNullOrEmpty(stage.assetPath))
                {
                    if (stage.prefabContentsRoot != null
                        && (go == stage.prefabContentsRoot || go.transform.IsChildOf(stage.prefabContentsRoot.transform)))
                        return stage.assetPath;
                }

                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (source != null)
                {
                    path = AssetDatabase.GetAssetPath(source);
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }

                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (instanceRoot != null)
                {
                    source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
                    if (source != null)
                        return AssetDatabase.GetAssetPath(source);
                }

                return null;
            }

            static ExportScope BuildScope(GameObject root, Action unload)
            {
                var rootRt = root.GetComponent<RectTransform>();
                if (rootRt != null)
                    CoordReverseTranslator.PrepareForExport(rootRt);

                return new ExportScope(root, unload);
            }
        }
    }
}
