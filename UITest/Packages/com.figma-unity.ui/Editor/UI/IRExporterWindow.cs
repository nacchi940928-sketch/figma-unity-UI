using System.Collections.Generic;
using System.IO;
using FigmaUnity.UI;
using FigmaUnity.UI.Editor.Config;
using FigmaUnity.UI.Editor.Figma;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Export
{
    public class IRExporterWindow : EditorWindow
    {
        GameObject _prefab;
        string _sourceExportDir;
        string _outputPath;
        string _statusMessage = "选择 Prefab 与原始 Figma 导出包，再点 Export to Figma XML。";
        [SerializeField] ExportProfile _exportProfile = ExportProfile.DefaultUnityToFigma();
        [SerializeField] FigmaDocumentFormat _sourceFormat = FigmaDocumentFormat.Auto;
        [SerializeField] FigmaDocumentFormat _outputFormat = FigmaDocumentFormat.Xml;
        [SerializeField] bool _alsoWriteJson;

        [MenuItem("Tools/Figma UI 导出 (Exporter)")]
        public static void Open()
        {
            GetWindow<IRExporterWindow>("Figma UI 导出");
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_sourceExportDir))
                _sourceExportDir = TryGetDefaultSourceExportDir();

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
                "默认输出 *-unity-export.xml，供 Figma 插件「Import & sync」使用。",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "源目录需含 Figma 导出的 *-full.xml（优先）或 *-full.json 作为模板。\n" +
                "坐标约定：x/y 为 Figma 左上角原点、Y 向下；Unity 根节点为屏幕中心锚点。",
                MessageType.None);

            _prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            _sourceExportDir = EditorGUILayout.TextField("Figma 源目录", _sourceExportDir);
            if (GUILayout.Button("浏览", GUILayout.Width(70)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择 Figma 导出目录", _sourceExportDir, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _sourceExportDir = picked;
                    UpdateOutputPath();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("使用样例导出目录"))
            {
                _sourceExportDir = TryGetDefaultSourceExportDir();
                UpdateOutputPath();
            }

            _sourceFormat = (FigmaDocumentFormat)EditorGUILayout.EnumPopup("源文件格式", _sourceFormat);
            _outputFormat = (FigmaDocumentFormat)EditorGUILayout.EnumPopup("输出格式", _outputFormat);
            if (_outputFormat != FigmaDocumentFormat.Auto)
                _alsoWriteJson = EditorGUILayout.ToggleLeft("同时输出另一种格式（JSON ↔ XML）", _alsoWriteJson);

            _outputPath = EditorGUILayout.TextField("输出文件", _outputPath);

            EditorGUILayout.Space();
            DrawExportProfileSection();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);

            GUI.enabled = _prefab != null && !string.IsNullOrWhiteSpace(_sourceExportDir);
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
                if (!FigmaExportPackage.TryLocate(_sourceExportDir, out var templatePath, out var screenName, sourceFormat))
                {
                    _statusMessage = "Source 目录中未找到 *-full.xml 或 *-full.json。";
                    return;
                }

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

                    var patches = PrefabIRExporter.Export(scope.Root);
                    if (patches == null || patches.Count == 0)
                    {
                        _statusMessage = "Prefab 中没有 IRBinding 节点。";
                        return;
                    }

                    _outputPath = ResolveOutputPath(
                        _sourceExportDir,
                        screenName,
                        templatePath,
                        outputFormat,
                        _outputPath);
                    if (PathsEqual(_outputPath, templatePath))
                    {
                        _statusMessage = "Output 不能与 *-full 模板相同，请另选输出路径。";
                        return;
                    }

                    var merge = FigmaJsonPatchMerger.MergeFile(
                        templatePath,
                        patches,
                        _outputPath,
                        new FigmaDocumentMerger.MergeOptions
                        {
                            ExportProfile = _exportProfile?.Clone() ?? ExportProfile.DefaultUnityToFigma()
                        });

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
                        $"文档节点: {merge.TotalElements}\n" +
                        $"已删除节点: {merge.RemovedCount}\n" +
                        $"坐标/尺寸变更: {merge.LayoutChangedCount}\n" +
                        $"约束/对齐变更: {merge.ConstraintsChangedCount}\n" +
                        $"Auto Layout 改绝对定位: {merge.AutoLayoutBrokenCount}\n" +
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

        void DrawExportProfileSection()
        {
            EditorGUILayout.LabelField("导出配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "与 Figma 插件「Import & sync」的勾选项对应。\n" +
                "★ 日常推荐【默认】：同步位置/文案，不写回约束（锚点由程序在 Unity 管）。",
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
            _exportProfile.SyncFills = EditorGUILayout.ToggleLeft("填充色（纯色）", _exportProfile.SyncFills);
            _exportProfile.SyncLayoutAdjustments = EditorGUILayout.ToggleLeft(
                "自动布局转绝对定位（layoutAdjustments）",
                _exportProfile.SyncLayoutAdjustments);
            _exportProfile.PruneMissingNodes = EditorGUILayout.ToggleLeft(
                "移除 Prefab 中已删除的节点",
                _exportProfile.PruneMissingNodes);
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
            if (FigmaExportPackage.TryLocate(
                    _sourceExportDir,
                    out var templatePath,
                    out var screenName,
                    _sourceFormat == FigmaDocumentFormat.Auto ? FigmaDocumentFormat.Auto : _sourceFormat))
            {
                var outputFormat = _outputFormat == FigmaDocumentFormat.Auto
                    ? FigmaDocumentSerializer.DetectFormat(templatePath)
                    : _outputFormat;
                _outputPath = BuildDefaultOutputPath(_sourceExportDir, screenName, templatePath, outputFormat);
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

        static string TryGetDefaultSourceExportDir()
        {
            var assets = Application.dataPath.Replace('\\', '/');
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/main-screen-1080x2340-export")),
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/examples/main-screen-1080x2340-export")),
                Path.GetFullPath(Path.Combine(assets, "../figmajson/main-screen-1080x2340-export"))
            };

            foreach (var dir in candidates)
            {
                if (FigmaExportPackage.TryLocate(dir, out _, out _))
                    return dir.Replace('\\', '/');
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
                    patch.width,
                    patch.height,
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
