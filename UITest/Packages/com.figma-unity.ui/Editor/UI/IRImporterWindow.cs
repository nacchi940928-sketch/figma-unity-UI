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
        [SerializeField] ImportProfile _importProfile = ImportProfile.VisualMerge();
        [SerializeField] string _artAssetRoot = ArtAssetResolver.DefaultArtRoot;

        [MenuItem("Tools/Figma UI 导入 (Importer)")]
        public static void Open()
        {
            GetWindow<IRImporterWindow>("Figma UI 导入");
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
                _importProfile = ImportProfile.VisualMerge();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Figma 导入", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择 Figma 导出的 *-full.xml 或 *-full.json。\n" +
                "同目录下的 PNG 等资源会一并用于图片节点。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _documentPath = EditorGUILayout.TextField("源文件", _documentPath);
            if (GUILayout.Button("浏览", GUILayout.Width(70)))
                BrowseSourceFile();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("使用样例 XML"))
                PickSampleDocument(preferXml: true);
            if (GUILayout.Button("使用样例 JSON"))
                PickSampleDocument(preferXml: false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _prefabPath = EditorGUILayout.TextField("Prefab 输出", _prefabPath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
                BrowsePrefabOutput();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("字体设置", EditorStyles.boldLabel);
            _defaultFont = (TMP_FontAsset)EditorGUILayout.ObjectField("默认字体", _defaultFont, typeof(TMP_FontAsset), false);
            _boldFont = (TMP_FontAsset)EditorGUILayout.ObjectField("粗体字体（可选）", _boldFont, typeof(TMP_FontAsset), false);
            _fontMapping = (FontMappingAsset)EditorGUILayout.ObjectField("字体映射（可选）", _fontMapping, typeof(FontMappingAsset), false);
            if (_defaultFont == null)
                EditorGUILayout.HelpBox("请选择默认字体，或先执行 Tools > Figma UI > Install Fonts from Assets/font。Figma 导出多为 Inter，含中文请使用支持 CJK 的 TMP 字体。", MessageType.Warning);
            if (GUILayout.Button("从 Assets/font 安装字体"))
                FontInstaller.InstallFromMenu();

            EditorGUILayout.Space();
            DrawArtAssetSection();

            EditorGUILayout.Space();
            DrawImportProfileSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_previewMessage, _previewMessageType);

            EditorGUILayout.Space();
            if (GUILayout.Button("校验"))
                RunPreview();

            var canImport = FigmaExportPackage.IsSupportedDocumentPath(_documentPath)
                && !string.IsNullOrWhiteSpace(_prefabPath);
            GUI.enabled = canImport;
            if (GUILayout.Button("导入到 Prefab", GUILayout.Height(32)))
                RunImport();
            GUI.enabled = true;

            if (!canImport)
                EditorGUILayout.HelpBox("导入需要有效的源文件与 Prefab 输出路径。", MessageType.None);
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

                var missing = FigmaExportPackage.FindMissingImageFiles(doc.node, assetDir, _artAssetRoot, screenName);
                var settings = new FigmaImportSettings
                {
                    CopyAssets = false,
                    ArtAssetRoot = _artAssetRoot
                };
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
                    $"Missing images: {missing.Length}" +
                    (missing.Length > 0 ? $" ({string.Join(", ", missing)})" : string.Empty) + "\n" +
                    $"Art root: {_artAssetRoot}\n" +
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
                var screenName = FigmaExportPackage.GetScreenNameFromDocumentPath(_documentPath);
                var layoutBoundsWarnings = FigmaLayoutBoundsValidator.ValidateDocument(
                    FigmaDocumentSerializer.Load(_documentPath));
                var settings = new FigmaImportSettings
                {
                    CopyAssets = true,
                    ArtAssetRoot = _artAssetRoot
                };
                var ir = FigmaToIRConverter.ConvertFile(_documentPath, settings, assetDir, screenName);
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
                        report.RemappedIdCount = mergeResult.RemappedIdCount;

                        foreach (var w in validation)
                            report.Warnings.Add("[validate] " + w);
                        foreach (var w in layoutBoundsWarnings)
                            report.Warnings.Add("[layout-bounds] " + w);

                        PrefabUtility.SaveAsPrefabAsset(root, _prefabPath);
                        SetPreview(
                            $"Merge 完成 → {_prefabPath}\n" +
                            $"更新: {mergeResult.UpdatedCount}  新建: {mergeResult.CreatedCount}  " +
                            $"删除: {mergeResult.RemovedCount}  保留 Unity 子节点: {mergeResult.PreservedUnityChildCount}" +
                            (mergeResult.RemappedIdCount > 0
                                ? $"  irId 重映射: {mergeResult.RemappedIdCount}"
                                : string.Empty) +
                            (report.MergePreservedAnchorsCount > 0
                                ? $"  保留锚点并同步布局: {report.MergePreservedAnchorsCount}"
                                : string.Empty),
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
                    foreach (var w in layoutBoundsWarnings)
                        report.Warnings.Add("[layout-bounds] " + w);

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

                if (layoutBoundsWarnings.Count > 0)
                {
                    var preview = layoutBoundsWarnings.Count > 8
                        ? string.Join("\n", layoutBoundsWarnings.GetRange(0, 8)) +
                          $"\n… 另有 {layoutBoundsWarnings.Count - 8} 条"
                        : string.Join("\n", layoutBoundsWarnings);
                    EditorUtility.DisplayDialog(
                        "布局坐标警告",
                        "以下节点的坐标/尺寸超出父节点范围，可能是导出时将绝对坐标误写为相对坐标：\n\n" +
                        preview,
                        "确定");
                }

                EditorUtility.DisplayDialog("Figma 导入完成", $"已保存 Prefab：\n{_prefabPath}", "确定");
            }
            catch (System.Exception ex)
            {
                SetPreview("Import 失败：\n" + ex.Message, MessageType.Error);
                EditorUtility.DisplayDialog("Figma 导入失败", ex.Message, "确定");
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
                        ? $"检测到 {extraComponents} 个自定义组件/脚本，合并时会全部保留。\n\n"
                        : "合并时会自动保留 Prefab 上已有的全部脚本与自定义组件。\n\n") +
                    "按 irId 更新 Figma 布局/视觉；无 IRBinding 的子物体也会保留。\n" +
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
            var screenName = FigmaExportPackage.GetScreenNameFromDocumentPath(_documentPath);
            return new PrefabBuildSettings
            {
                PrefabOutputPath = _prefabPath,
                ImportProfile = _importProfile?.Clone() ?? ImportProfile.Full(),
                ArtAssetRoot = _artAssetRoot,
                GeneratedRoot = "Assets/UI/Generated",
                FigmaExportDir = FigmaExportPackage.GetAssetDirectory(_documentPath),
                ScreenName = screenName,
                Fonts = new FontBuildSettings
                {
                    DefaultFont = _defaultFont,
                    BoldFont = _boldFont,
                    Mapping = _fontMapping
                }
            };
        }

        void DrawArtAssetSection()
        {
            EditorGUILayout.LabelField("美术资源", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "按 Figma XML 中 fills.imageFile 的文件名，在下方 Unity 目录查找贴图并挂到 Prefab。\n" +
                "查找顺序：美术资源目录 → 美术资源目录/界面名 → 已生成目录 → Figma 导出包目录。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            _artAssetRoot = EditorGUILayout.TextField("美术资源目录", _artAssetRoot);
            if (GUILayout.Button("选择", GUILayout.Width(48)))
                BrowseArtAssetRoot();
            EditorGUILayout.EndHorizontal();
        }

        void BrowseArtAssetRoot()
        {
            var picked = EditorUtility.OpenFolderPanel(
                "选择美术资源目录（Unity Assets 下）",
                GetUnityAssetsAbsolutePath(_artAssetRoot),
                string.Empty);
            if (string.IsNullOrEmpty(picked))
                return;

            _artAssetRoot = ToAssetsRelativePath(picked);
        }

        static string GetUnityAssetsAbsolutePath(string assetsRelativePath)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativePath))
                return Application.dataPath;

            assetsRelativePath = assetsRelativePath.Replace('\\', '/');
            if (!assetsRelativePath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(assetsRelativePath, "Assets", System.StringComparison.OrdinalIgnoreCase))
                return Application.dataPath;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? Application.dataPath
                : Path.GetFullPath(Path.Combine(projectRoot, assetsRelativePath)).Replace('\\', '/');
        }

        static string ToAssetsRelativePath(string absolutePath)
        {
            absolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            var dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!absolutePath.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
                return absolutePath;

            var relative = "Assets" + absolutePath.Substring(dataPath.Length);
            return relative.Replace('\\', '/');
        }

        void DrawImportProfileSection()
        {
            EditorGUILayout.LabelField("导入配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择 Figma 数据如何应用到 Unity Prefab。\n" +
                "★ 日常推荐【视觉合并】：Figma 改尺寸/字号/对齐后会同步到 Prefab。\n" +
                "★ 若程序已手动改过锚点且要保留：勾选「合并时保留锚点」。\n" +
                "★ 首次导入或整页重做：选【静态绝对布局】，下方点【全量重建】。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("视觉合并\n(日常推荐)"))
                _importProfile = ImportProfile.VisualMerge();
            if (GUILayout.Button("全量导入\n(含约束/布局)"))
                _importProfile = ImportProfile.Full();
            if (GUILayout.Button("静态绝对布局\n(首次导入)"))
                _importProfile = ImportProfile.StaticAbsolute();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("重建模式", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(
                    _importProfile.RebuildMode == ImportRebuildMode.Merge,
                    "合并更新",
                    EditorStyles.miniButtonLeft))
                _importProfile.RebuildMode = ImportRebuildMode.Merge;
            if (GUILayout.Toggle(
                    _importProfile.RebuildMode == ImportRebuildMode.Replace,
                    "全量重建",
                    EditorStyles.miniButtonRight))
                _importProfile.RebuildMode = ImportRebuildMode.Replace;
            EditorGUILayout.EndHorizontal();

            if (_importProfile.RebuildMode == ImportRebuildMode.Merge)
            {
                _importProfile.PruneMissingNodes = EditorGUILayout.ToggleLeft(
                    "移除 Figma 中已删除的节点",
                    _importProfile.PruneMissingNodes);
                _importProfile.PreserveAnchorsOnMerge = EditorGUILayout.ToggleLeft(
                    "合并时保留锚点（按 Figma 坐标更新位置/尺寸）",
                    _importProfile.PreserveAnchorsOnMerge);
                EditorGUILayout.HelpBox(
                    "合并更新：按节点 id 匹配并更新 Figma 布局/视觉。\n" +
                    "自动保留：Prefab 上全部脚本与自定义组件、无 IRBinding 的子物体、程序挂的锚点/适配（见上项）。无需额外勾选。",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "全量重建：删除旧 Prefab 后按 Figma 完整重建，自定义组件（Button、脚本等）会丢失。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("高级选项（一般保持视觉合并预设即可）", EditorStyles.miniLabel);
            _importProfile.ApplyConstraints = EditorGUILayout.ToggleLeft(
                "应用 Figma 约束 → Unity 锚点（程序自建锚点时请关闭）",
                _importProfile.ApplyConstraints);
            _importProfile.ApplyAutoLayoutFill = EditorGUILayout.ToggleLeft(
                "应用自动布局 FILL → LayoutGroup + LayoutElement",
                _importProfile.ApplyAutoLayoutFill);
            _importProfile.ApplyTypography = EditorGUILayout.ToggleLeft(
                "应用字体样式（字号/粗细/字体资源）",
                _importProfile.ApplyTypography);
            _importProfile.ApplyTextAlignment = EditorGUILayout.ToggleLeft(
                "应用文字对齐（TMP 水平/垂直对齐）",
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
