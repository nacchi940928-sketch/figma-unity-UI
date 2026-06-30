using System.Collections.Generic;
using System.IO;
using FigmaUnity.UI.Editor.Build;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.UI
{
    public static class FontInstaller
    {
        const string FontFolder = "Assets/font";
        const string MappingAssetPath = FontFolder + "/FontMapping.asset";

        [MenuItem("Tools/Figma UI/Install Fonts from Assets/font")]
        public static void InstallFromMenu()
        {
            var report = Install();
            var message = string.Join("\n", report);
            EditorUtility.DisplayDialog(
                report.Count > 0 && report[0].StartsWith("Error") ? "Font Install Failed" : "Font Install",
                message,
                "OK");
        }

        /// <summary>
        /// Batchmode entry: Unity.exe -batchmode -quit -projectPath ... -executeMethod FigmaUnity.UI.Editor.UI.FontInstaller.InstallFromBatchMode
        /// </summary>
        public static void InstallFromBatchMode()
        {
            var report = Install();
            foreach (var line in report)
                Debug.Log("[FontInstaller] " + line);
        }

        public static List<string> Install()
        {
            var report = new List<string>();

            if (!EnsureTmpEssentialResources(report))
                return report;

            if (!AssetDatabase.IsValidFolder(FontFolder))
            {
                report.Add($"Error: folder not found: {FontFolder}");
                return report;
            }

            var fontPaths = Directory.GetFiles(FontFolder, "*.*", SearchOption.TopDirectoryOnly);
            var sdfAssets = new List<TMP_FontAsset>();

            foreach (var path in fontPaths)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf")
                    continue;

                var assetPath = path.Replace('\\', '/');
                var sdfPath = GetSdfAssetPath(assetPath);
                var sdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
                if (sdf == null)
                {
                    sdf = CreateSdfFontAsset(assetPath, sdfPath, report);
                    if (sdf == null)
                        continue;
                }
                else
                {
                    report.Add($"Exists: {sdfPath}");
                }

                sdfAssets.Add(sdf);
            }

            if (sdfAssets.Count == 0)
            {
                report.Add($"No .ttf/.otf found in {FontFolder}. Copy font files there first.");
                return report;
            }

            var mapping = AssetDatabase.LoadAssetAtPath<FontMappingAsset>(MappingAssetPath);
            if (mapping == null)
            {
                mapping = ScriptableObject.CreateInstance<FontMappingAsset>();
                AssetDatabase.CreateAsset(mapping, MappingAssetPath);
                report.Add($"Created: {MappingAssetPath}");
            }

            var primary = sdfAssets[0];
            EnsureMappingEntry(mapping, "Inter", primary, report);

            EditorUtility.SetDirty(mapping);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Add($"Default TMP font: {AssetDatabase.GetAssetPath(primary)}");
            report.Add("In Figma UI Importer, assign Default Font and Font Mapping (optional auto-load on next open).");
            return report;
        }

        static bool EnsureTmpEssentialResources(List<string> report)
        {
            if (TMP_Settings.instance != null && Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
                return true;

            report.Add("Importing TMP Essential Resources (one-time setup)...");

            try
            {
                TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
                AssetDatabase.Refresh();
                TMPro_EventManager.ON_RESOURCES_LOADED();
            }
            catch (System.Exception ex)
            {
                report.Add("Error: failed to import TMP Essential Resources: " + ex.Message);
                report.Add("Please run Window > TextMeshPro > Import TMP Essential Resources, then retry.");
                return false;
            }

            if (TMP_Settings.instance == null || Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                report.Add("Error: TMP Essential Resources are still missing.");
                report.Add("Please run Window > TextMeshPro > Import TMP Essential Resources, then retry.");
                return false;
            }

            report.Add("TMP Essential Resources ready.");
            return true;
        }

        static string GetSdfAssetPath(string fontAssetPath)
        {
            var folder = Path.GetDirectoryName(fontAssetPath)?.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(fontAssetPath);
            return $"{folder}/{name} SDF.asset";
        }

        static TMP_FontAsset CreateSdfFontAsset(string sourceFontPath, string sdfPath, List<string> report)
        {
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
            if (sourceFont == null)
            {
                report.Add($"Error: cannot load Font at {sourceFontPath}");
                return null;
            }

            TMP_FontAsset fontAsset;
            try
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            }
            catch (System.Exception ex)
            {
                report.Add($"Error: CreateFontAsset failed for {sourceFontPath}: {ex.Message}");
                report.Add("Run Window > TextMeshPro > Import TMP Essential Resources, then retry.");
                return null;
            }

            if (fontAsset == null)
            {
                report.Add($"Error: CreateFontAsset failed for {sourceFontPath}. Enable Include Font Data in import settings.");
                return null;
            }

            if (fontAsset.material == null)
            {
                report.Add("Error: TMP shader not found. Run Window > TextMeshPro > Import TMP Essential Resources, then retry.");
                Object.DestroyImmediate(fontAsset);
                return null;
            }

            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(sdfPath);
            var assetName = Path.GetFileNameWithoutExtension(uniquePath);

            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                fontAsset.atlasTextures[0].name = assetName + " Atlas";

            if (fontAsset.material != null)
                fontAsset.material.name = assetName + " Atlas Material";

            AssetDatabase.CreateAsset(fontAsset, uniquePath);

            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);

            if (fontAsset.material != null)
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            EditorUtility.SetDirty(fontAsset);
            report.Add($"Created: {uniquePath}");
            return fontAsset;
        }

        static void EnsureMappingEntry(FontMappingAsset mapping, string figmaFamily, TMP_FontAsset font, List<string> report)
        {
            mapping.entries ??= new List<FontMappingEntry>();

            foreach (var entry in mapping.entries)
            {
                if (entry != null && entry.Matches(figmaFamily))
                {
                    if (entry.font != font)
                    {
                        entry.font = font;
                        report.Add($"Updated mapping: {figmaFamily} -> {AssetDatabase.GetAssetPath(font)}");
                    }
                    else
                    {
                        report.Add($"Mapping unchanged: {figmaFamily}");
                    }
                    return;
                }
            }

            mapping.entries.Add(new FontMappingEntry
            {
                figmaFamily = figmaFamily,
                font = font
            });
            report.Add($"Added mapping: {figmaFamily} -> {AssetDatabase.GetAssetPath(font)}");
        }

        public static TMP_FontAsset TryGetInstalledDefaultFont()
        {
            var mapping = AssetDatabase.LoadAssetAtPath<FontMappingAsset>(MappingAssetPath);
            if (mapping?.entries != null)
            {
                foreach (var entry in mapping.entries)
                {
                    if (entry?.font != null)
                        return entry.font;
                }
            }

            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(" SDF.asset"))
                    return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            }

            return null;
        }

        public static FontMappingAsset TryGetInstalledMapping()
        {
            return AssetDatabase.LoadAssetAtPath<FontMappingAsset>(MappingAssetPath);
        }
    }
}
