using System.Collections.Generic;
using System.IO;
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

            if (string.IsNullOrEmpty(figmaExportDir))
                return null;

            var exportSource = Path.Combine(figmaExportDir, imageFile);
            if (!File.Exists(exportSource))
                return null;

            if (!copyFromExportDir)
                return null;

            return AssetResolver.CopyImage(figmaExportDir, imageFile, screenName, generatedRoot);
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
            File.Copy(sourceFull, destFull, true);
            return true;
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

            if (!string.IsNullOrEmpty(artAssetRoot))
            {
                yield return $"{artAssetRoot}/{imageFile}";
                if (!string.IsNullOrEmpty(screenName))
                    yield return $"{artAssetRoot}/{screenName}/{imageFile}";
            }

            if (!string.IsNullOrEmpty(generatedRoot) && !string.IsNullOrEmpty(screenName))
                yield return $"{generatedRoot}/{screenName}/{imageFile}";
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
            return AssetDatabase.LoadAssetAtPath<Texture>(assetPath) != null
                || AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) != null;
        }
    }
}
