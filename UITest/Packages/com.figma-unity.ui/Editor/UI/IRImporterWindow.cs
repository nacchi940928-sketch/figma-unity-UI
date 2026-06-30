using System.IO;
using FigmaUnity.UI;
using FigmaUnity.UI.Editor.Build;
using FigmaUnity.UI.Editor.Config;
using FigmaUnity.UI.Editor.Figma;
using FigmaUnity.UI.Editor.IR;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.UI
{
    public class IRImporterWindow : EditorWindow
    {
        string _documentPath;
        string _prefabPath = "Assets/UI/Generated/main.prefab";
        string _previewMessage = "请选择 Figma 导出的 XML 或 JSON 文件，再点 Validate。";
        MessageType _previewMessageType = MessageType.Info;

        [SerializeField] TMP_FontAsset _defaultFont;
        [SerializeField] TMP_FontAsset _boldFont;
        [SerializeField] FontMappingAsset _fontMapping;
        [SerializeField] ImportProfile _importProfile = ImportProfile.Full();

        [MenuItem("Tools/Figma UI Importer")]
        public static void Open()
        {
            GetWindow<IRImporterWindow>("Figma UI Importer");
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_documentPath))
                _documentPath = TryGetDefaultSampleDocumentPath();
            if (_defaultFont == null)
                _defaultFont = FontInstaller.TryGetInstalledDefaultFont() ?? TryGetDefaultTmpFont();
            if (_fontMapping == null)
                _fontMapping = FontInstaller.TryGetInstalledMapping();
            if (_importProfile == null)
                _importProfile = ImportProfile.Full();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Figma Import", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "直接选择 Figma 导出的 *-full.xml 或 *-full.json。\n" +
                "同目录下的 PNG 等资源会一并用于图片节点。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _documentPath = EditorGUILayout.TextField("Source File", _documentPath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
                BrowseSourceFile();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Sample XML"))
                PickSampleDocument(preferXml: true);
            if (GUILayout.Button("Use Sample JSON"))
                PickSampleDocument(preferXml: false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _prefabPath = EditorGUILayout.TextField("Prefab Output", _prefabPath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
                BrowsePrefabOutput();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Font Settings", EditorStyles.boldLabel);
            _defaultFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Default Font", _defaultFont, typeof(TMP_FontAsset), false);
            _boldFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Bold Font (optional)", _boldFont, typeof(TMP_FontAsset), false);
            _fontMapping = (FontMappingAsset)EditorGUILayout.ObjectField("Font Mapping (optional)", _fontMapping, typeof(FontMappingAsset), false);
            if (_defaultFont == null)
                EditorGUILayout.HelpBox("请选择 Default Font，或先执行 Tools > Figma UI > Install Fonts from Assets/font。Figma 导出多为 Inter，含中文请使用支持 CJK 的 TMP Font Asset。", MessageType.Warning);
            if (GUILayout.Button("Install Fonts from Assets/font"))
                FontInstaller.InstallFromMenu();

            EditorGUILayout.Space();
            DrawImportProfileSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_previewMessage, _previewMessageType);

            EditorGUILayout.Space();
            if (GUILayout.Button("Validate"))
                RunPreview();

            var canImport = FigmaExportPackage.IsSupportedDocumentPath(_documentPath)
                && !string.IsNullOrWhiteSpace(_prefabPath);
            GUI.enabled = canImport;
            if (GUILayout.Button("Import", GUILayout.Height(32)))
                RunImport();
            GUI.enabled = true;

            if (!canImport)
                EditorGUILayout.HelpBox("Import 需要有效的 Source File 与 Prefab Output。", MessageType.None);
        }

        void BrowseSourceFile()
        {
            var startDir = GetDocumentDirectory();
            var picked = EditorUtility.OpenFilePanel(
                "Select Figma Export (XML or JSON)",
                startDir,
                "xml,json");
            if (string.IsNullOrEmpty(picked))
                return;

            _documentPath = picked.Replace('\\', '/');
            SuggestPrefabPathFromDocument(_documentPath);
            RunPreview();
        }

        void BrowsePrefabOutput()
        {
            var dir = "Assets/UI/Generated";
            if (!string.IsNullOrEmpty(_prefabPath))
            {
                var parent = Path.GetDirectoryName(_prefabPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent) && parent.StartsWith("Assets"))
                    dir = parent;
            }

            var fileName = string.IsNullOrEmpty(_prefabPath)
                ? "main"
                : Path.GetFileNameWithoutExtension(_prefabPath);
            var picked = EditorUtility.SaveFilePanelInProject(
                "Prefab Output",
                fileName,
                "prefab",
                "Choose where to save the imported prefab.",
                dir);
            if (!string.IsNullOrEmpty(picked))
                _prefabPath = picked.Replace('\\', '/');
        }

        void PickSampleDocument(bool preferXml)
        {
            var sample = TryGetDefaultSampleDocumentPath(preferXml);
            if (string.IsNullOrEmpty(sample))
            {
                SetPreview("未找到样例文件 figmajson/testjson/*-full.xml 或 *-full.json。", MessageType.Warning);
                return;
            }

            _documentPath = sample;
            SuggestPrefabPathFromDocument(_documentPath);
            RunPreview();
        }

        void SuggestPrefabPathFromDocument(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath))
                return;

            var screenName = FigmaExportPackage.GetScreenNameFromDocumentPath(documentPath);
            _prefabPath = $"Assets/UI/Generated/{screenName}.prefab";
        }

        void RunPreview()
        {
            if (!FigmaExportPackage.IsSupportedDocumentPath(_documentPath))
            {
                SetPreview("请选择有效的 *-full.xml 或 *-full.json 文件。", MessageType.Warning);
                return;
            }

            try
            {
                var assetDir = FigmaExportPackage.GetAssetDirectory(_documentPath);
                var screenName = FigmaExportPackage.GetScreenNameFromDocumentPath(_documentPath);
                var doc = FigmaDocumentSerializer.LoadDocument(_documentPath);
                if (doc?.node == null)
                {
                    SetPreview("文档解析失败：缺少 node 字段。", MessageType.Error);
                    return;
                }

                var missing = FigmaExportPackage.FindMissingImageFiles(doc.node, assetDir);
                var settings = new FigmaImportSettings { CopyAssets = false };
                var ir = FigmaToIRConverter.ConvertFile(_documentPath, settings, assetDir, screenName);
                var warnings = IRValidator.Validate(ir);
                var nodeCount = CountNodes(ir);
                var formatLabel = GetFormatLabel(_documentPath);

                var prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath) != null;
                var prefabNote = prefabExists
                    ? $"\nPrefab 已存在: {_prefabPath}（Import 时需确认覆盖）"
                    : string.Empty;

                SetPreview(
                    $"Screen: {screenName}\n" +
                    $"Document: {Path.GetFileName(_documentPath)} ({formatLabel})\n" +
                    $"Asset dir: {assetDir}\n" +
                    $"Modified: {File.GetLastWriteTime(_documentPath):yyyy-MM-dd HH:mm:ss}\n" +
                    $"Expected nodes: {doc.metadata?.totalElements ?? 0}\n" +
                    $"IR nodes: {nodeCount}\n" +
                    $"IR root: {ir.id}\n" +
                    $"Missing images: {missing.Length}\n" +
                    $"Validation warnings: {warnings.Count}" +
                    prefabNote,
                    missing.Length > 0 || warnings.Count > 0 ? MessageType.Warning : MessageType.Info);

                Debug.Log($"[Figma UI Importer] Validate OK: {screenName}, nodes={nodeCount}, doc={_documentPath}");
            }
            catch (System.Exception ex)
            {
                SetPreview("Validate 失败：\n" + ex.Message, MessageType.Error);
                Debug.LogException(ex);
            }
        }

        void RunImport()
        {
            if (!FigmaExportPackage.IsSupportedDocumentPath(_documentPath))
            {
                SetPreview("Source File 无效。", MessageType.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_prefabPath))
            {
                SetPreview("请填写 Prefab Output。", MessageType.Error);
                return;
            }

            if (!ConfirmOverwriteIfNeeded())
                return;

            try
            {
                var assetDir = FigmaExportPackage.GetAssetDirectory(_documentPath);
                var settings = new FigmaImportSettings { CopyAssets = true };
                var ir = FigmaToIRConverter.ConvertFile(_documentPath, settings, assetDir);
                var validation = IRValidator.Validate(ir);
                var buildSettings = CreateBuildSettings();
                var profile = buildSettings.ImportProfile;
                EnsurePrefabFolder(_prefabPath);

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
                var useMerge = profile.RebuildMode == ImportRebuildMode.Merge && existing != null;
                ImportReport report;

                if (useMerge)
                {
                    var root = PrefabUtility.LoadPrefabContents(_prefabPath);
                    try
                    {
                        var mergeResult = PrefabMerger.Merge(root, ir, buildSettings);
                        report = mergeResult.Report;
                        report.MergeUpdatedCount = mergeResult.UpdatedCount;
                        report.MergeCreatedCount = mergeResult.CreatedCount;
                        report.MergeRemovedCount = mergeResult.RemovedCount;
                        report.MergePreservedUnityChildren = mergeResult.PreservedUnityChildCount;

                        foreach (var w in validation)
                            report.Warnings.Add("[validate] " + w);

                        PrefabUtility.SaveAsPrefabAsset(root, _prefabPath);
                        SetPreview(
                            $"Merge 完成 → {_prefabPath}\n" +
                            $"更新: {mergeResult.UpdatedCount}  新建: {mergeResult.CreatedCount}  " +
                            $"删除: {mergeResult.RemovedCount}  保留 Unity 子节点: {mergeResult.PreservedUnityChildCount}",
                            MessageType.Info);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                else
                {
                    var build = PrefabBuilder.Build(ir, buildSettings);
                    report = build.Report;

                    foreach (var w in validation)
                        report.Warnings.Add("[validate] " + w);

                    if (existing != null)
                        AssetDatabase.DeleteAsset(_prefabPath);

                    PrefabUtility.SaveAsPrefabAsset(build.Root, _prefabPath);
                    Object.DestroyImmediate(build.Root);
                    SetPreview($"Replace 完成 → {_prefabPath}", MessageType.Info);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
                Selection.activeObject = prefab;
                report.LogSummary();
                EditorUtility.DisplayDialog("Figma UI Import", $"Saved prefab:\n{_prefabPath}", "OK");
            }
            catch (System.Exception ex)
            {
                SetPreview("Import 失败：\n" + ex.Message, MessageType.Error);
                EditorUtility.DisplayDialog("Figma UI Import Failed", ex.Message, "OK");
                Debug.LogException(ex);
            }
        }

        bool ConfirmOverwriteIfNeeded()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
            if (existing == null)
                return true;

            var bindingCount = existing.GetComponentsInChildren<IRBinding>(true).Length;
            var extraComponents = CountExtraComponents(existing);
            var merge = _importProfile.RebuildMode == ImportRebuildMode.Merge;

            if (merge)
            {
                return EditorUtility.DisplayDialog(
                    "合并更新 Prefab？",
                    $"目标：{_prefabPath}\n\n" +
                    $"当前 Prefab 含 {bindingCount} 个 IRBinding 节点。\n" +
                    (extraComponents > 0
                        ? $"检测到约 {extraComponents} 个自定义组件（如 Button、脚本），Merge 模式下会保留。\n\n"
                        : "\n") +
                    "Merge：按 irId 更新 Figma 布局/视觉；无 IRBinding 的子物体会保留。\n" +
                    "Figma 中已删除的 IR 节点可从 Prefab 移除（见 Prune 选项）。\n\n" +
                    "是否继续？",
                    "合并更新",
                    "取消");
            }

            return EditorUtility.DisplayDialog(
                "覆盖已有 Prefab？",
                $"目标：{_prefabPath}\n\n" +
                $"当前 Prefab 含 {bindingCount} 个 IRBinding 节点。\n" +
                (extraComponents > 0
                    ? $"另有约 {extraComponents} 个自定义组件，Replace 模式下会全部丢失。\n\n"
                    : "\n") +
                "Replace 会删除旧 Prefab 并按 XML/JSON 全量重建。\n\n" +
                "是否继续？",
                "覆盖重建",
                "取消");
        }

        static int CountExtraComponents(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return 0;

            var count = 0;
            foreach (var behaviour in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (behaviour == null)
                    continue;
                if (behaviour is Transform or RectTransform or CanvasRenderer or IRBinding)
                    continue;
                if (behaviour is UnityEngine.UI.Graphic or UnityEngine.UI.LayoutGroup
                    or UnityEngine.UI.LayoutElement or CanvasGroup)
                    continue;
                if (behaviour is TMP_Text)
                    continue;
                count++;
            }

            return count;
        }

        static string GetFormatLabel(string documentPath)
        {
            if (!FigmaDocumentSerializer.IsXmlPath(documentPath))
                return "JSON";

            var text = File.ReadAllText(documentPath);
            if (FigmaDesignExportXmlSerializer.CanParse(text))
                return "XML (Figma design-export)";
            return "XML (Unity figma-export)";
        }

        string GetDocumentDirectory()
        {
            var dir = FigmaExportPackage.GetAssetDirectory(_documentPath);
            return string.IsNullOrEmpty(dir) ? Application.dataPath : dir;
        }

        void SetPreview(string message, MessageType type)
        {
            _previewMessage = message;
            _previewMessageType = type;
            Repaint();
        }

        static int CountNodes(IRNode node)
        {
            if (node == null) return 0;
            var count = 1;
            if (node.children == null) return count;
            foreach (var child in node.children)
                count += CountNodes(child);
            return count;
        }

        static string TryGetDefaultSampleDocumentPath(bool? preferXml = null)
        {
            var assets = Application.dataPath.Replace('\\', '/');
            var dirs = new[]
            {
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/testjson")),
                Path.GetFullPath(Path.Combine(assets, "../figmajson/testjson")),
                Path.GetFullPath(Path.Combine(assets, "../../figmajson/examples/main-screen-1080x2340-export"))
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                if (preferXml != false)
                {
                    var xml = Path.Combine(dir, "main-screen-1080x2340-full.xml");
                    if (File.Exists(xml))
                        return xml.Replace('\\', '/');
                    if (FigmaExportPackage.TryLocate(dir, out var xmlPath, out _, FigmaDocumentFormat.Xml))
                        return xmlPath.Replace('\\', '/');
                }

                if (preferXml != true)
                {
                    var json = Path.Combine(dir, "main-full.json");
                    if (File.Exists(json))
                        return json.Replace('\\', '/');
                    if (FigmaExportPackage.TryLocate(dir, out var jsonPath, out _, FigmaDocumentFormat.Json))
                        return jsonPath.Replace('\\', '/');
                }
            }

            return null;
        }

        static TMP_FontAsset TryGetDefaultTmpFont()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        }

        PrefabBuildSettings CreateBuildSettings()
        {
            return new PrefabBuildSettings
            {
                PrefabOutputPath = _prefabPath,
                ImportProfile = _importProfile?.Clone() ?? ImportProfile.Full(),
                Fonts = new FontBuildSettings
                {
                    DefaultFont = _defaultFont,
                    BoldFont = _boldFont,
                    Mapping = _fontMapping
                }
            };
        }

        void DrawImportProfileSection()
        {
            EditorGUILayout.LabelField("Import Profile", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Figma 导出全量 XML/JSON；此处选择哪些语义应用到 Unity Prefab。\n" +
                "几何与视觉（位置/尺寸/颜色/圆角）始终导入。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Full Import"))
                _importProfile = ImportProfile.Full();
            if (GUILayout.Button("Static Absolute"))
                _importProfile = ImportProfile.StaticAbsolute();
            EditorGUILayout.EndHorizontal();

            _importProfile.RebuildMode = (ImportRebuildMode)EditorGUILayout.EnumPopup(
                "Rebuild Mode",
                _importProfile.RebuildMode);
            if (_importProfile.RebuildMode == ImportRebuildMode.Merge)
            {
                _importProfile.PruneMissingNodes = EditorGUILayout.ToggleLeft(
                    "Remove IR nodes deleted in Figma",
                    _importProfile.PruneMissingNodes);
                EditorGUILayout.HelpBox(
                    "Merge：按 irId 更新布局/视觉；保留 Button、脚本及无 IRBinding 的子物体。",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Replace：删除旧 Prefab 并全量重建，自定义组件会丢失。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            _importProfile.ApplyConstraints = EditorGUILayout.ToggleLeft(
                "Constraints → Unity anchors",
                _importProfile.ApplyConstraints);
            _importProfile.ApplyAutoLayoutFill = EditorGUILayout.ToggleLeft(
                "Auto Layout FILL → LayoutGroup + LayoutElement",
                _importProfile.ApplyAutoLayoutFill);
            _importProfile.ApplyTypography = EditorGUILayout.ToggleLeft(
                "Typography → TMP font / size / bold",
                _importProfile.ApplyTypography);
            _importProfile.ApplyTextAlignment = EditorGUILayout.ToggleLeft(
                "Text alignment → TMP alignment",
                _importProfile.ApplyTextAlignment);
        }

        static void EnsurePrefabFolder(string prefabPath)
        {
            prefabPath = prefabPath.Replace('\\', '/');
            var dir = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir))
                return;

            var parts = dir.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
