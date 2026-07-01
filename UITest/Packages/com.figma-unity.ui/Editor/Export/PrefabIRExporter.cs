using System.Collections.Generic;
using FigmaUnity.UI;
using FigmaUnity.UI.Editor.Build;
using FigmaUnity.UI.Editor.Figma;
using FigmaUnity.UI.Editor.IR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUnity.UI.Editor.Export
{
    public class UnityNodePatch
    {
        public string irId;
        public string figmaNodeId;
        public string name;
        public string parentIrId;
        public bool isUnityAdded;
        public int hierarchyDepth;
        public string type;
        public bool visible;
        public float x;
        public float y;
        public float rootX;
        public float rootY;
        public float width;
        public float height;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float rotation;
        public float opacity;
        public string constraintHorizontal = "MIN";
        public string constraintVertical = "MIN";
        public string imageFile;
        public string imageHash;
        public string imageScaleMode = "FILL";
        public string imageUnityAssetPath;
        public List<IRFill> fills = new List<IRFill>();
        public IRText text;
    }

    public static class PrefabIRExporter
    {
        public static Dictionary<string, UnityNodePatch> Export(GameObject prefabRoot)
        {
            var patches = new Dictionary<string, UnityNodePatch>();
            if (prefabRoot == null)
                return patches;

            var prefabRootRt = prefabRoot.GetComponent<RectTransform>();
            var bindings = prefabRoot.GetComponentsInChildren<IRBinding>(true);
            foreach (var binding in bindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.irId))
                    continue;

                var rt = binding.GetComponent<RectTransform>();
                if (rt == null)
                    continue;

                RectTransform parentRt = null;
                if (binding.gameObject != prefabRoot)
                    parentRt = rt.transform.parent?.GetComponent<RectTransform>();

                var patch = ExtractPatch(binding.gameObject, rt, parentRt, prefabRootRt, binding);
                if (UnityNodeBindingCompleter.IsUnityAddedBinding(binding))
                {
                    patch.isUnityAdded = true;
                    patch.parentIrId = UnityNodeBindingCompleter.FindParentIrId(rt.transform, prefabRoot);
                    patch.hierarchyDepth = UnityNodeBindingCompleter.GetHierarchyDepth(rt.transform, prefabRoot.transform);
                }

                patches[binding.irId] = patch;
            }

            return patches;
        }

        public static void EnrichVisuals(GameObject prefabRoot, Dictionary<string, UnityNodePatch> patches)
        {
            if (prefabRoot == null || patches == null)
                return;

            foreach (var binding in prefabRoot.GetComponentsInChildren<IRBinding>(true))
            {
                if (binding == null || string.IsNullOrEmpty(binding.irId))
                    continue;
                if (!patches.TryGetValue(binding.irId, out var patch))
                    continue;

                var go = binding.gameObject;
                patch.visible = go.activeSelf;
                patch.opacity = ResolveOpacity(go);

                if (go.TryGetComponent<TextMeshProUGUI>(out var tmp))
                {
                    patch.type = "text";
                    patch.text = BuildTextContentPatch(tmp);
                }
                else if (go.TryGetComponent<RawImage>(out var raw) && raw.texture != null)
                {
                    patch.type = "image";
                    ApplyImagePatchFromComponents(patch, raw, null);
                }
                else if (go.TryGetComponent<Image>(out var image))
                {
                    if (image.sprite != null)
                    {
                        patch.type = "image";
                        ApplyImagePatchFromComponents(patch, null, image);
                    }
                    else if (image.color.a > 0.01f)
                    {
                        patch.type = "frame";
                        patch.fills.Clear();
                        patch.fills.Add(new IRFill
                        {
                            type = "solid",
                            color = ColorUtil.ToHex(image.color),
                            opacity = image.color.a
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Overwrites patch layout from live RectTransform geometry (fixes LayoutGroup sizeDelta=0).
        /// </summary>
        public static void ApplyLayoutFromLiveRoot(GameObject prefabRoot, Dictionary<string, UnityNodePatch> patches)
        {
            if (prefabRoot == null || patches == null)
                return;

            var rootRt = prefabRoot.GetComponent<RectTransform>();
            if (rootRt != null)
                CoordReverseTranslator.PrepareForExport(rootRt);

            foreach (var binding in prefabRoot.GetComponentsInChildren<IRBinding>(true))
            {
                if (binding == null || string.IsNullOrEmpty(binding.irId))
                    continue;
                if (!patches.TryGetValue(binding.irId, out var patch))
                    continue;

                var rt = binding.GetComponent<RectTransform>();
                if (rt == null)
                    continue;

                RectTransform parentRt = null;
                if (binding.gameObject != prefabRoot)
                    parentRt = rt.transform.parent?.GetComponent<RectTransform>();

                ScaleExportHelper.ExportLayout(patch, rt, parentRt, rootRt);
                patch.rotation = -rt.localEulerAngles.z;

                var serialized = RectTransformSerializedReader.Read(rt);
                ConstraintReverseTranslator.ReadConstraints(serialized, out patch.constraintHorizontal, out patch.constraintVertical);
            }
        }

        static UnityNodePatch ExtractPatch(
            GameObject go,
            RectTransform rt,
            RectTransform parentRt,
            RectTransform prefabRootRt,
            IRBinding binding)
        {
            var patch = ExtractPatchFromTransform(go, rt, parentRt, prefabRootRt);
            patch.irId = binding.irId;
            patch.figmaNodeId = binding.figmaNodeId;
            patch.name = go.name;
            return patch;
        }

        static UnityNodePatch ExtractPatchFromTransform(
            GameObject go,
            RectTransform rt,
            RectTransform parentRt,
            RectTransform prefabRootRt)
        {
            var serialized = RectTransformSerializedReader.Read(rt);
            ConstraintReverseTranslator.ReadConstraints(serialized, out var horizontal, out var vertical);

            var patch = new UnityNodePatch
            {
                name = go.name,
                visible = go.activeSelf,
                rotation = -rt.localEulerAngles.z,
                opacity = ResolveOpacity(go),
                constraintHorizontal = horizontal,
                constraintVertical = vertical
            };

            ScaleExportHelper.ExportLayout(patch, rt, parentRt, prefabRootRt);

            if (go.TryGetComponent<TextMeshProUGUI>(out var tmp))
            {
                patch.type = "text";
                patch.text = BuildTextContentPatch(tmp);
            }
            else if (go.TryGetComponent<RawImage>(out var raw) && raw.texture != null)
            {
                patch.type = "image";
                ApplyImagePatchFromComponents(patch, raw, null);
            }
            else if (go.TryGetComponent<Image>(out var image))
            {
                if (image.sprite != null)
                {
                    patch.type = "image";
                    ApplyImagePatchFromComponents(patch, null, image);
                }
                else if (image.color.a > 0.01f)
                {
                    patch.type = "frame";
                    patch.fills.Add(new IRFill
                    {
                        type = "solid",
                        color = ColorUtil.ToHex(image.color),
                        opacity = image.color.a
                    });
                }
            }

            return patch;
        }

        static void ApplyImagePatchFromComponents(UnityNodePatch patch, RawImage raw, Image image)
        {
            string unityAssetPath = null;
            string fileName = null;

            if (raw != null && raw.texture != null)
            {
                unityAssetPath = UnityEditor.AssetDatabase.GetAssetPath(raw.texture);
                fileName = ArtAssetResolver.GetTextureFileName(raw.texture);
                if (raw.color.a < 1f)
                    patch.opacity *= raw.color.a;
            }
            else if (image != null && image.sprite != null)
            {
                unityAssetPath = UnityEditor.AssetDatabase.GetAssetPath(image.sprite);
                fileName = ArtAssetResolver.GetSpriteFileName(image.sprite);
                if (image.color.a < 1f)
                    patch.opacity *= image.color.a;
            }

            if (string.IsNullOrEmpty(fileName))
                return;

            if (ArtAssetResolver.IsProceduralRoundedAsset(fileName, unityAssetPath))
            {
                patch.imageFile = null;
                patch.imageUnityAssetPath = null;
                patch.type = "frame";
                patch.fills.Clear();
                var color = raw != null ? raw.color : image.color;
                if (color.a > 0.01f)
                {
                    patch.fills.Add(new IRFill
                    {
                        type = "solid",
                        color = ColorUtil.ToHex(color),
                        opacity = color.a
                    });
                }

                return;
            }

            patch.imageFile = fileName;
            patch.imageUnityAssetPath = unityAssetPath?.Replace('\\', '/');
            patch.imageScaleMode = "FILL";
        }

        static float ResolveOpacity(GameObject go)
        {
            if (go.TryGetComponent<CanvasGroup>(out var group))
                return group.alpha;
            return 1f;
        }

        static IRText BuildTextContentPatch(TextMeshProUGUI tmp)
        {
            TextAlignReverseMapper.FromTmp(tmp, out var horizontal, out var vertical);
            return new IRText
            {
                content = tmp.text,
                align = horizontal.ToLowerInvariant(),
                alignVertical = vertical.ToLowerInvariant()
            };
        }
    }
}
