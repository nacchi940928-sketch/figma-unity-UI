using System;
using System.Collections.Generic;
using System.IO;
using FigmaUnity.UI;
using FigmaUnity.UI.Editor.Config;
using FigmaUnity.UI.Editor.Figma;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Serialization;

namespace FigmaUnity.UI.Editor.Export
{
    public class IRExporterWindow : EditorWindow
    {
        GameObject _prefab;
        [FormerlySerializedAs("_sourceExportDir")]
        string _sourceDocumentPath;
        string _outputPath;
        string _statusMessage = "选择 Prefab 与 Figma 源文件（XML/JSON），再点「导出到 Figma」。";
        [SerializeField] ExportProfile _exportProfile = ExportProfile.DefaultUnityToFigma();
        [SerializeField] FigmaDocumentFormat _sourceFormat = FigmaDocumentFormat.Auto;
        [SerializeField] FigmaDocumentFormat _outputFormat = FigmaDocumentFormat.Xml;
        [SerializeField] bool _alsoWriteJson;
        [SerializeField] string _artAssetRoot = ArtAssetResolver.DefaultArtRoot;
        List<UnityNodeBindingInfo> _pendingNodes = new List<UnityNodeBindingInfo>();
        GameObject _lastScannedPrefab;

        [MenuItem("Tools/Figma UI 导出 (Exporter)")]
        public static void Open()
        {
            GetWindow<IRExporterWindow>("Figma UI 导出");
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_sourceDocumentPath))
                _sourceDocumentPath = TryGetDefaultSourceDocumentPath();

            if (_prefab == null && Selection.activeObject is GameObject go)
                _prefab = go;

            if (_exportProfile == null)
                _exportProfile = ExportProfile.DefaultUnityToFigma();

            UpdateOutputPath();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Unity → Figma 导出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "把 Unity Prefab 的改动写回 Figma 用的 XML/JSON。\n" +
                "改完 Prefab 务必 Ctrl+S 保存；在 Prefab 编辑模式导出最准确。\n" +
                "Unity 新增的节点需先点「补全节点」添加 IRBinding，再导出到 Figma。",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "选择 Figma 导出的 XML 或 JSON 作为合并模板（文件名不限，不必是 *-full）。\n" +
                "PNG 等资源从源文件同目录解析；坐标约定：x/y 为 Figma 左上角原点、Y 向下。",
                MessageType.None);

            _prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            _sourceDocumentPath = EditorGUILayout.TextField("Figma 源文件", _sourceDocumentPath);
            if (GUILayout.Button("浏览", GUILayout.Width(70)))
                BrowseSourceFile();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("使用样例源文件"))
            {
                _sourceDocumentPath = TryGetDefaultSourceDocumentPath();
                UpdateOutputPath();
            }

            _sourceFormat = (FigmaDocumentFormat)EditorGUILayout.EnumPopup("源文件格式", _sourceFormat);
            _outputFormat = (FigmaDocumentFormat)EditorGUILayout.EnumPopup("输出格式", _outputFormat);
            if (_outputFormat != FigmaDocumentFormat.Auto)
                _alsoWriteJson = EditorGUILayout.ToggleLeft("同时输出另一种格式（JSON ↔ XML）", _alsoWriteJson);

            _outputPath = EditorGUILayout.TextField("输出文件", _outputPath);

            EditorGUILayout.BeginHorizontal();
            _artAssetRoot = EditorGUILayout.TextField("美术资源目录（Unity）", _artAssetRoot);
            if (GUILayout.Button("选择", GUILayout.Width(48)))
                BrowseArtAssetRoot();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "导出 XML 不会写入 Assets 路径；只写文件名 imageFile。\n" +
                "PNG 会尝试复制到 XML 同目录（可选）；Figma 插件只需 metadata.assetFileNames 中的文件名，在用户自选资源文件夹里按名匹配。",
                MessageType.None);

            EditorGUILayout.Space();
            DrawNodeBindingSection();

            EditorGUILayout.Space();
            DrawExportProfileSection();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);

            GUI.enabled = _prefab != null && FigmaExportPackage.IsSupportedDocumentPath(_sourceDocumentPath);
            if (GUILayout.Button("导出到 Figma", GUILayout.Height(32)))
                RunExport();
            GUI.enabled = true;
        }

        void RunExport()
        {
            try
            {
                var sourceFormat = _sourceFormat == FigmaDocumentFormat.Auto
                    ? FigmaDocumentFormat.Auto
                    : _sourceFormat;
                if (!FigmaExportPackage.TryResolveSource(_sourceDocumentPath, out var templatePath, out var screenName, sourceFormat))
                {
                    _statusMessage = sourceFormat == FigmaDocumentFormat.Auto
                        ? "请选择有效的 Figma 源文件（.xml 或 .json）。"
                        : $"源文件不存在，或与所选格式（{sourceFormat}）不一致。";
                    return;
                }

                var assetDir = FigmaExportPackage.GetAssetDirectory(templatePath);

                var outputFormat = _outputFormat == FigmaDocumentFormat.Auto
                    ? FigmaDocumentSerializer.DetectFormat(templatePath)
                    : _outputFormat;

                var assetPath = ExportHierarchyUtil.ExportScope.ResolvePrefabAssetPath(_prefab);

                using (var scope = ExportHierarchyUtil.ExportScope.Create(_prefab))
                {
                    if (scope.Root == null)
                    {
                        _statusMessage = "请选择有效的 Prefab。";
                        return;
                    }

                    var profile = _exportProfile?.Clone() ?? ExportProfile.DefaultUnityToFigma();
                    var patches = PrefabIRExporter.Export(scope.Root);
                    if (patches == null || patches.Count == 0)
                    {
                        _statusMessage = "Prefab 中没有可导出的 UI 节点。新增节点请先点「补全节点」。";
                        return;
                    }

                    var pendingUnityAdded = 0;
                    foreach (var patch in patches.Values)
                    {
                        if (patch.isUnityAdded)
                            pendingUnityAdded++;
                    }

                    if (profile.SyncUnityAddedNodes && pendingUnityAdded > 0)
                    {
                        Debug.Log($"[Figma UI Exporter] 将写入 Figma 的新增节点: {pendingUnityAdded}");
                    }
                    else if (pendingUnityAdded > 0)
                    {
                        Debug.LogWarning(
                            $"[Figma UI Exporter] 有 {pendingUnityAdded} 个已补全节点未勾选「写入 Figma 的新增节点」，本次不会创建 Figma 图层。");
                    }

                    _outputPath = ResolveOutputPath(
                        assetDir,
                        screenName,
                        templatePath,
                        outputFormat,
                        _outputPath);
                    if (PathsEqual(_outputPath, templatePath))
                    {
                        _statusMessage = "输出文件不能与源模板相同，请另选输出路径。";
                        return;
                    }

                    var merge = FigmaJsonPatchMerger.MergeFile(
                        templatePath,
                        patches,
                        _outputPath,
                        new FigmaDocumentMerger.MergeOptions
                        {
                            ExportProfile = profile
                        },
                        _artAssetRoot);

                    string alternatePath = null;
                    if (_alsoWriteJson && outputFormat != FigmaDocumentFormat.Auto)
                    {
                        alternatePath = BuildAlternateExportPath(_outputPath, outputFormat);
                        var document = FigmaDocumentSerializer.Load(_outputPath);
                        FigmaDocumentSerializer.Save(document, alternatePath);
                    }

                    LogSamplePatch(patches, "inspiration-bubble__6_121");
                    WritePatchesSidecar(_outputPath, patches);
                    VerifyMergedLayout(_outputPath, patches, "inspiration-bubble__6_121");

                    if (patches.TryGetValue("inspiration-bubble__6_121", out var bubblePatch))
                    {
                        Debug.Log(
                            "[Figma UI Exporter] inspiration-bubble patch " +
                            $"x={bubblePatch.x}, y={bubblePatch.y}, " +
                            $"constraints={bubblePatch.constraintHorizontal}/{bubblePatch.constraintVertical}");
                    }
                    else
                    {
                        Debug.LogWarning("[Figma UI Exporter] inspiration-bubble__6_121 missing from patches.");
                    }

                    var fileSizeKb = new FileInfo(_outputPath).Length / 1024f;
                    _statusMessage =
                        $"Export 完成\n" +
                        $"主文件: {_outputPath}\n" +
                        (alternatePath != null ? $"副本: {alternatePath}\n" : string.Empty) +
                        $"格式: {outputFormat}\n" +
                        $"大小: {fileSizeKb:F0} KB\n" +
                        $"Unity 节点: {patches.Count}\n" +
                        $"新增 Figma 节点: {merge.AddedCount}\n" +
                        $"文档节点: {merge.TotalElements}\n" +
                        $"已删除节点: {merge.RemovedCount}\n" +
                        $"坐标/尺寸变更: {merge.LayoutChangedCount}\n" +
                        $"约束/对齐变更: {merge.ConstraintsChangedCount}\n" +
                        $"Auto Layout 改绝对定位: {merge.AutoLayoutBrokenCount}\n" +
                        $"图片字段更新: {merge.ImagesChangedCount}\n" +
                        $"图片文件导出: {merge.ImagesExportedCount}\n" +
                        $"Updated: {merge.UpdatedCount}\n" +
                        $"Warnings: {merge.Warnings.Count}";

                    if (merge.LayoutChangedCount == 0 && merge.ConstraintsChangedCount == 0)
                        Debug.LogWarning("[Figma UI Exporter] 坐标与约束与模板完全一致。若你在 Unity 里改过对齐/位置，请确认 Prefab 已 Ctrl+S 保存后再导出。");

                    Debug.Log($"[Figma UI Exporter] Wrote {_outputPath}");
                    EditorUtility.RevealInFinder(_outputPath);

                    foreach (var warning in merge.Warnings)
                        Debug.LogWarning("[Figma UI Exporter] " + warning);

                    EditorUtility.DisplayDialog("Figma 导出完成", _statusMessage, "确定");
                }
            }
            catch (System.Exception ex)
            {
                _statusMessage = "Export 失败：\n" + ex.Message;
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Figma 导出失败", ex.Message, "确定");
            }
        }

        void DrawNodeBindingSection()
        {
            EditorGUILayout.LabelField("节点补全", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在 Unity 里手动新增的 UI 节点（如 red-point）没有 IRBinding，Figma 无法识别。\n" +
                "请先点「补全节点」，工具会添加 IRBinding 并告知节点名称与 irId。",
                MessageType.None);

            RefreshPendingNodesIfNeeded();

            if (_prefab == null)
            {
                EditorGUILayout.HelpBox("请先选择 Prefab。", MessageType.None);
            }
            else if (_pendingNodes.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有待补全节点。", MessageType.Info);
            }
            else
            {
                var preview = new System.Text.StringBuilder();
                preview.AppendLine($"待补全 {_pendingNodes.Count} 个：");
                foreach (var node in _pendingNodes)
                    preview.AppendLine("• " + node.objectName + "  （路径: " + node.hierarchyPath + "）");
                EditorGUILayout.HelpBox(preview.ToString(), MessageType.Warning);
            }

            GUI.enabled = _prefab != null;
            if (GUILayout.Button("补全节点", GUILayout.Height(28)))
                RunCompleteBindings();
            GUI.enabled = true;
        }

        void RefreshPendingNodesIfNeeded()
        {
            if (_prefab == _lastScannedPrefab)
                return;

            _lastScannedPrefab = _prefab;
            _pendingNodes = ScanPendingNodes(_prefab);
        }

        static List<UnityNodeBindingInfo> ScanPendingNodes(GameObject prefab)
        {
            if (prefab == null)
                return new List<UnityNodeBindingInfo>();

            using (var scope = ExportHierarchyUtil.ExportScope.Create(prefab))
            {
                if (scope.Root == null)
                    return new List<UnityNodeBindingInfo>();
                return UnityNodeBindingCompleter.FindPendingNodes(scope.Root);
            }
        }

        void RunCompleteBindings()
        {
            try
            {
                var assetPath = ExportHierarchyUtil.ExportScope.ResolvePrefabAssetPath(_prefab);
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                List<UnityNodeBindingInfo> completed;

                if (stage?.prefabContentsRoot != null
                    && (_prefab == stage.prefabContentsRoot
                        || _prefab.transform.IsChildOf(stage.prefabContentsRoot.transform)))
                {
                    completed = UnityNodeBindingCompleter.CompleteBindings(stage.prefabContentsRoot);
                    if (completed.Count > 0)
                    {
                        EditorUtility.SetDirty(stage.prefabContentsRoot);
                        PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                    }
                }
                else if (!string.IsNullOrEmpty(assetPath))
                {
                    var root = PrefabUtility.LoadPrefabContents(assetPath);
                    try
                    {
                        completed = UnityNodeBindingCompleter.CompleteBindings(root);
                        if (completed.Count > 0)
                            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                else
                {
                    using (var scope = ExportHierarchyUtil.ExportScope.Create(_prefab))
                    {
                        if (scope.Root == null)
                        {
                            _statusMessage = "请选择有效的 Prefab。";
                            return;
                        }

                        completed = UnityNodeBindingCompleter.CompleteBindings(scope.Root);
                    }

                    if (completed.Count > 0)
                    {
                        _statusMessage = "已补全节点，但未能自动保存 Prefab。请在 Prefab 编辑模式中操作或手动保存。";
                        EditorUtility.DisplayDialog(
                            "节点补全",
                            UnityNodeBindingCompleter.FormatBindingSummary(completed) +
                            "\n\n请手动保存 Prefab（Ctrl+S）。",
                            "确定");
                        return;
                    }
                }

                _lastScannedPrefab = null;
                RefreshPendingNodesIfNeeded();

                var summary = UnityNodeBindingCompleter.FormatBindingSummary(completed);
                _statusMessage = completed.Count > 0
                    ? "节点补全完成\n" + summary
                    : "没有需要补全的节点。";

                foreach (var node in completed)
                {
                    Debug.Log(
                        "[Figma UI Exporter] 补全节点: " + node.objectName +
                        " → irId=" + node.irId +
                        ", 父级=" + node.parentName +
                        " (" + node.parentIrId + ")");
                }

                EditorUtility.DisplayDialog(
                    completed.Count > 0 ? "节点补全完成" : "节点补全",
                    summary + (completed.Count > 0 ? "\n\nPrefab 已保存，可继续「导出到 Figma」。" : string.Empty),
                    "确定");
            }
            catch (Exception ex)
            {
                _statusMessage = "节点补全失败：\n" + ex.Message;
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("节点补全失败", ex.Message, "确定");
            }
        }

        void DrawExportProfileSection()
        {
            EditorGUILayout.LabelField("导出配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "与 Figma 插件「Import & sync」的勾选项对应。\n" +
                "★ 日常推荐【默认】：同步位置/文案/美术资源；不勾填充色、不勾约束。\n" +
                "Figma 大量节点 fills 为空（靠子图层/贴图表现），勾填充色容易整屏变色。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("默认\n(推荐)"))
                _exportProfile = ExportProfile.DefaultUnityToFigma();
            if (GUILayout.Button("仅布局+文案"))
                _exportProfile = ExportProfile.LayoutAndTextOnly();
            EditorGUILayout.EndHorizontal();

            _exportProfile.SyncTransform = EditorGUILayout.ToggleLeft("位置 / 尺寸 / 旋转", _exportProfile.SyncTransform);
            _exportProfile.SyncVisibility = EditorGUILayout.ToggleLeft("可见性 / 透明度", _exportProfile.SyncVisibility);
            _exportProfile.SyncConstraints = EditorGUILayout.ToggleLeft(
                "约束（一般关闭，锚点由程序在 Unity 设置）",
                _exportProfile.SyncConstraints);
            _exportProfile.SyncTextContent = EditorGUILayout.ToggleLeft("文案内容", _exportProfile.SyncTextContent);
            _exportProfile.SyncTextAlignment = EditorGUILayout.ToggleLeft("文字对齐", _exportProfile.SyncTextAlignment);
            _exportProfile.SyncTypography = EditorGUILayout.ToggleLeft("字体样式（fontFamily / fontSize）", _exportProfile.SyncTypography);
            _exportProfile.SyncFills = EditorGUILayout.ToggleLeft(
                "填充色（仅更新 Figma 原本有 SOLID 的节点，勿给空 fill 节点加色）",
                _exportProfile.SyncFills);
            _exportProfile.SyncImageAssets = EditorGUILayout.ToggleLeft(
                "美术资源（imageFile + 导出 PNG 到输出目录）",
                _exportProfile.SyncImageAssets);
            _exportProfile.SyncLayoutAdjustments = EditorGUILayout.ToggleLeft(
                "自动布局转绝对定位（layoutAdjustments）",
                _exportProfile.SyncLayoutAdjustments);
            _exportProfile.SyncUnityAddedNodes = EditorGUILayout.ToggleLeft(
                "写入 Figma 的新增节点（已补全且 figmaNodeId 为空）",
                _exportProfile.SyncUnityAddedNodes);
            _exportProfile.PruneMissingNodes = EditorGUILayout.ToggleLeft(
                "移除 Prefab 中已删除的节点",
                _exportProfile.PruneMissingNodes);
        }

        void BrowseSourceFile()
        {
            var startDir = GetSourceDirectory();
            var picked = EditorUtility.OpenFilePanel(
                "选择 Figma 源文件（XML 或 JSON）",
                startDir,
                "xml,json");
            if (string.IsNullOrEmpty(picked))
                return;

            _sourceDocumentPath = picked.Replace('\\', '/');
            UpdateOutputPath();
        }

        string GetSourceDirectory()
        {
            var dir = FigmaExportPackage.GetAssetDirectory(_sourceDocumentPath);
            return string.IsNullOrEmpty(dir) ? Application.dataPath : dir;
        }

        void BrowseArtAssetRoot()
        {
            var picked = EditorUtility.OpenFolderPanel(
                "选择美术资源目录（Unity Assets 下）",
                Path.GetFullPath(Path.Combine(Application.dataPath, "UI/Art")),
                string.Empty);
            if (string.IsNullOrEmpty(picked))
                return;

            var dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            picked = Path.GetFullPath(picked).Replace('\\', '/');
            _artAssetRoot = picked.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + picked.Substring(dataPath.Length)
                : picked;
        }

        static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            return string.Equals(
                Path.GetFullPath(a).Replace('\\', '/'),
                Path.GetFullPath(b).Replace('\\', '/'),
                System.StringComparison.OrdinalIgnoreCase);
        }

        void UpdateOutputPath()
        {
            if (FigmaExportPackage.TryResolveSource(
                    _sourceDocumentPath,
                    out var templatePath,
                    out var screenName,
                    _sourceFormat == FigmaDocumentFormat.Auto ? FigmaDocumentFormat.Auto : _sourceFormat))
            {
                var assetDir = FigmaExportPackage.GetAssetDirectory(templatePath);
                var outputFormat = _outputFormat == FigmaDocumentFormat.Auto
                    ? FigmaDocumentSerializer.DetectFormat(templatePath)
                    : _outputFormat;
                _outputPath = BuildDefaultOutputPath(assetDir, screenName, templatePath, outputFormat);
            }
        }

        static string BuildDefaultOutputPath(
            string dir,
            string screenName,
            string templatePath,
            FigmaDocumentFormat outputFormat)
        {
            return ResolveOutputPath(dir, screenName, templatePath, outputFormat, null);
        }

        static string ResolveOutputPath(
            string dir,
            string screenName,
            string templatePath,
            FigmaDocumentFormat outputFormat,
            string userPath)
        {
            return FigmaExportPackage.ResolveUnityExportPath(dir, screenName, templatePath, outputFormat, userPath);
        }

        static string BuildAlternateExportPath(string primaryPath, FigmaDocumentFormat primaryFormat)
        {
            if (primaryFormat == FigmaDocumentFormat.Xml)
                return Path.ChangeExtension(primaryPath, ".json");
            return Path.ChangeExtension(primaryPath, ".xml");
        }

        static string TryGetDefaultSourceDocumentPath()
        {
            var assets = Application.dataPath.Replace('\\', '/');
            var dirs = new[]
            {
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/testjson")),
                Path.GetFullPath(Path.Combine(assets, "../figmajson/testjson")),
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/main-screen-1080x2340-export")),
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/examples/main-screen-1080x2340-export"))
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                var xml = Path.Combine(dir, "main-screen-1080x2340-full.xml");
                if (File.Exists(xml))
                    return xml.Replace('\\', '/');

                if (FigmaExportPackage.TryLocate(dir, out var docPath, out _))
                    return docPath.Replace('\\', '/');
            }

            return string.Empty;
        }

        static void LogSamplePatch(Dictionary<string, UnityNodePatch> patches, string irId)
        {
            if (!patches.TryGetValue(irId, out var patch))
            {
                Debug.Log($"[Figma UI Exporter] Patch missing: {irId}");
                return;
            }

            Debug.Log(
                $"[Figma UI Exporter] {irId} -> x={patch.x}, y={patch.y}, " +
                $"constraints={patch.constraintHorizontal}/{patch.constraintVertical}");
        }

        static void WritePatchesSidecar(string outputPath, Dictionary<string, UnityNodePatch> patches)
        {
            if (string.IsNullOrEmpty(outputPath) || patches == null)
                return;

            var sidecarPath = Path.ChangeExtension(outputPath, ".patches.json");
            var rows = new List<object>();
            foreach (var patch in patches.Values)
            {
                rows.Add(new
                {
                    patch.irId,
                    patch.x,
                    patch.y,
                    patch.rootX,
                    patch.rootY,
                    patch.width,
                    patch.height,
                    patch.scaleX,
                    patch.scaleY,
                    patch.constraintHorizontal,
                    patch.constraintVertical
                });
            }

            File.WriteAllText(
                sidecarPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
        }

        static void VerifyMergedLayout(
            string outputPath,
            Dictionary<string, UnityNodePatch> patches,
            string irId)
        {
            if (string.IsNullOrEmpty(outputPath) || patches == null || !patches.TryGetValue(irId, out var patch))
                return;
            if (!File.Exists(outputPath))
                return;

            var root = FigmaDocumentSerializer.Load(outputPath);
            var node = root["node"] as Newtonsoft.Json.Linq.JObject;
            if (node == null || !TryFindNodeByIrId(node, irId, out var target))
            {
                Debug.LogWarning($"[Figma UI Exporter] Verify failed: {irId} not found in output document.");
                return;
            }

            var mergedY = target.Value<float?>("y") ?? 0f;
            if (System.Math.Abs(mergedY - patch.y) > 0.01f)
            {
                Debug.LogError(
                    $"[Figma UI Exporter] Merge verify failed for {irId}: " +
                    $"expected y={patch.y}, got y={mergedY} in {outputPath}");
            }
        }

        static bool TryFindNodeByIrId(
            Newtonsoft.Json.Linq.JObject node,
            string irId,
            out Newtonsoft.Json.Linq.JObject found)
        {
            found = null;
            if (node == null)
                return false;

            if (irId == node.Value<string>("irId"))
            {
                found = node;
                return true;
            }

            if (node["children"] is not Newtonsoft.Json.Linq.JArray children)
                return false;

            foreach (var childToken in children)
            {
                if (childToken is not Newtonsoft.Json.Linq.JObject child)
                    continue;
                if (TryFindNodeByIrId(child, irId, out found))
                    return true;
            }

            return false;
        }

        static RectTransform FindBindingTransform(GameObject root, string irId)
        {
            if (root == null)
                return null;

            foreach (var binding in root.GetComponentsInChildren<IRBinding>(true))
            {
                if (binding.irId == irId)
                    return binding.GetComponent<RectTransform>();
            }

            return null;
        }
    }
}
