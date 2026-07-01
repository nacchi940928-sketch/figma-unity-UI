using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Resolves Figma imageFile names to Unity texture assets and copies textures into export packages.
    /// Lookup order: ArtAssetRoot → ArtAssetRoot/screenName → GeneratedRoot/screenName → Figma export dir.
    /// </summary>
    public static class ArtAssetResolver
    {
        public const string DefaultArtRoot = "Assets/UI/Art";

        public static string ResolveUnityAssetPath(
            string imageFile,
            string artAssetRoot,
            string screenName,
            string figmaExportDir,
            string generatedRoot,
            bool copyFromExportDir)
        {
            if (string.IsNullOrWhiteSpace(imageFile))
                return null;

            imageFile = Path.GetFileName(imageFile.Trim());

            foreach (var candidate in EnumerateCandidates(artAssetRoot, screenName, imageFile, generatedRoot))
            {
                if (UnityAssetExists(candidate))
                    return candidate.Replace('\\', '/');
            }

            if (!string.IsNullOrEmpty(figmaExportDir))
            {
                var exportMatch = FindFileCaseInsensitive(figmaExportDir, imageFile);
                if (exportMatch != null && copyFromExportDir)
                    return AssetResolver.CopyImage(figmaExportDir, Path.GetFileName(exportMatch), screenName, generatedRoot);
            }

            return null;
        }

        public static string FindExistingFile(string directory, string fileName)
        {
            return FindFileCaseInsensitive(directory, fileName);
        }

        static string FindFileCaseInsensitive(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory))
                return null;

            foreach (var path in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }

        public static string GetTextureFileName(Texture texture)
        {
            if (texture == null)
                return null;

            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (assetPath.Contains("unity_builtin") || assetPath == "Library/unity default resources")
                return null;

            if (texture == Texture2D.whiteTexture || texture == Texture2D.blackTexture)
                return null;

            return Path.GetFileName(assetPath);
        }

        public static string GetSpriteFileName(Sprite sprite)
        {
            if (sprite == null)
                return null;

            var assetPath = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            return Path.GetFileName(assetPath);
        }

        public static bool CopyUnityAssetToExportDir(string unityAssetPath, string exportDir, string imageFile)
        {
            if (string.IsNullOrWhiteSpace(unityAssetPath) || string.IsNullOrWhiteSpace(exportDir))
                return false;

            imageFile = Path.GetFileName(imageFile?.Trim() ?? Path.GetFileName(unityAssetPath));
            if (string.IsNullOrEmpty(imageFile))
                return false;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return false;

            unityAssetPath = unityAssetPath.Replace('\\', '/');
            var sourceFull = Path.GetFullPath(Path.Combine(projectRoot, unityAssetPath));
            if (!File.Exists(sourceFull))
                return false;

            Directory.CreateDirectory(exportDir);
            var destFull = Path.GetFullPath(Path.Combine(exportDir, imageFile));
            if (!FileCopyHelper.TryCopy(sourceFull, destFull, out var error))
            {
                Debug.LogWarning($"[Figma UI] {error}");
                return File.Exists(destFull);
            }

            return true;
        }

        public static bool IsProceduralRoundedAsset(string imageFile, string unityAssetPath)
        {
            if (!string.IsNullOrEmpty(unityAssetPath))
            {
                var path = unityAssetPath.Replace('\\', '/');
                if (path.Contains("/_roundedSprites/", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (string.IsNullOrEmpty(imageFile))
                return false;

            return Regex.IsMatch(Path.GetFileName(imageFile), @"^rounded_\d+\.png$", RegexOptions.IgnoreCase);
        }

        public static string[] FindMissingFiles(string documentAssetDir, IEnumerable<string> imageFiles)
        {
            var missing = new List<string>();
            if (imageFiles == null)
                return missing.ToArray();

            foreach (var imageFile in imageFiles)
            {
                if (string.IsNullOrWhiteSpace(imageFile))
                    continue;

                var fileName = Path.GetFileName(imageFile.Trim());
                var path = Path.Combine(documentAssetDir ?? string.Empty, fileName);
                if (!File.Exists(path))
                    missing.Add(fileName);
            }

            return missing.ToArray();
        }

        static IEnumerable<string> EnumerateCandidates(
            string artAssetRoot,
            string screenName,
            string imageFile,
            string generatedRoot)
        {
            artAssetRoot = NormalizeFolder(artAssetRoot);
            generatedRoot = NormalizeFolder(generatedRoot);
            screenName = screenName?.Trim();

            foreach (var folder in EnumerateSearchFolders(artAssetRoot, screenName, generatedRoot))
            {
                var match = FindAssetPathInFolder(folder, imageFile);
                if (!string.IsNullOrEmpty(match))
                    yield return match;
            }
        }

        static IEnumerable<string> EnumerateSearchFolders(string artAssetRoot, string screenName, string generatedRoot)
        {
            if (!string.IsNullOrEmpty(artAssetRoot))
            {
                yield return artAssetRoot;
                if (!string.IsNullOrEmpty(screenName))
                    yield return $"{artAssetRoot}/{screenName}";
            }

            if (!string.IsNullOrEmpty(generatedRoot) && !string.IsNullOrEmpty(screenName))
                yield return $"{generatedRoot}/{screenName}";
        }

        static string FindAssetPathInFolder(string assetFolder, string imageFile)
        {
            assetFolder = NormalizeFolder(assetFolder);
            if (string.IsNullOrEmpty(assetFolder))
                return null;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            var absoluteFolder = Path.GetFullPath(Path.Combine(projectRoot, assetFolder));
            var match = FindFileCaseInsensitive(absoluteFolder, imageFile);
            if (match == null)
                return null;

            var relative = ToAssetsRelativePath(match);
            return string.IsNullOrEmpty(relative) ? null : relative;
        }

        static string ToAssetsRelativePath(string absolutePath)
        {
            absolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            var dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return ("Assets" + absolutePath.Substring(dataPath.Length)).Replace('\\', '/');
        }

        static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return string.Empty;
            return folder.Replace('\\', '/').TrimEnd('/');
        }

        static bool UnityAssetExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return false;

            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(absolutePath))
                return false;

            return AssetDatabase.LoadAssetAtPath<Texture>(assetPath) != null
                || AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) != null;
        }
    }
}
