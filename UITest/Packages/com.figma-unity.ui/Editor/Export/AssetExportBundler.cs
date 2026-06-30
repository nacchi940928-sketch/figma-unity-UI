using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FigmaUnity.UI.Editor.Figma;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Export
{
    /// <summary>
    /// Ensures every fills.imageFile in the export document has a PNG beside the XML (assetDir).
    /// XML never stores Unity project paths — only filenames + metadata.assetDir.
    /// </summary>
    public static class AssetExportBundler
    {
        public sealed class BundleResult
        {
            public int CopiedCount;
            public readonly List<string> AssetFiles = new List<string>();
            public readonly List<string> MissingFiles = new List<string>();
        }

        public static BundleResult Bundle(
            JObject root,
            string exportDir,
            string templatePath,
            string artAssetRoot,
            Dictionary<string, UnityNodePatch> patches)
        {
            var result = new BundleResult();
            if (root == null || string.IsNullOrEmpty(exportDir))
                return result;

            Directory.CreateDirectory(exportDir);
            exportDir = Path.GetFullPath(exportDir).Replace('\\', '/');
            var templateDir = string.IsNullOrEmpty(templatePath)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(templatePath))?.Replace('\\', '/');

            var unityPathByFile = BuildUnityPathIndex(patches);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImageFiles(root["node"] as JObject, referenced);
            NormalizeImageFileNames(root["node"] as JObject);

            foreach (var imageFile in referenced.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = NormalizeFileName(imageFile);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                var destFull = Path.Combine(exportDir, fileName);
                if (File.Exists(destFull))
                {
                    result.AssetFiles.Add(fileName);
                    continue;
                }

                if (TryCopyFromKnownSources(fileName, imageFile, exportDir, templateDir, artAssetRoot, unityPathByFile))
                {
                    result.CopiedCount++;
                    result.AssetFiles.Add(fileName);
                }
                else
                {
                    result.MissingFiles.Add(fileName);
                }
            }

            return result;
        }

        static Dictionary<string, string> BuildUnityPathIndex(Dictionary<string, UnityNodePatch> patches)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (patches == null)
                return index;

            foreach (var patch in patches.Values)
            {
                if (string.IsNullOrEmpty(patch.imageFile) || string.IsNullOrEmpty(patch.imageUnityAssetPath))
                    continue;
                if (ArtAssetResolver.IsProceduralRoundedAsset(patch.imageFile, patch.imageUnityAssetPath))
                    continue;

                index[patch.imageFile] = patch.imageUnityAssetPath;
            }

            return index;
        }

        static bool TryCopyFromKnownSources(
            string fileName,
            string originalName,
            string exportDir,
            string templateDir,
            string artAssetRoot,
            Dictionary<string, string> unityPathByFile)
        {
            if (unityPathByFile.TryGetValue(originalName, out var unityPath)
                && ArtAssetResolver.CopyUnityAssetToExportDir(unityPath, exportDir, fileName))
                return true;

            if (!string.IsNullOrEmpty(artAssetRoot))
            {
                var resolved = ArtAssetResolver.ResolveUnityAssetPath(
                    fileName,
                    artAssetRoot,
                    screenName: null,
                    figmaExportDir: null,
                    generatedRoot: null,
                    copyFromExportDir: false);
                if (!string.IsNullOrEmpty(resolved)
                    && ArtAssetResolver.CopyUnityAssetToExportDir(resolved, exportDir, fileName))
                    return true;
            }

            if (!string.IsNullOrEmpty(templateDir))
            {
                foreach (var candidate in CandidateNames(fileName, originalName))
                {
                    var source = Path.Combine(templateDir, candidate);
                    if (!File.Exists(source))
                        continue;

                    File.Copy(source, Path.Combine(exportDir, fileName), true);
                    return true;
                }
            }

            return false;
        }

        static IEnumerable<string> CandidateNames(string fileName, string originalName)
        {
            yield return fileName;
            if (!string.Equals(fileName, originalName, StringComparison.OrdinalIgnoreCase))
                yield return originalName;
        }

        static void CollectImageFiles(JObject node, HashSet<string> referenced)
        {
            if (node == null)
                return;

            if (node["fills"] is JArray fills)
            {
                foreach (var token in fills)
                {
                    if (token is not JObject fill)
                        continue;
                    if (!string.Equals(fill.Value<string>("type"), "IMAGE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var imageFile = fill.Value<string>("imageFile");
                    if (!string.IsNullOrWhiteSpace(imageFile))
                        referenced.Add(imageFile.Trim());
                }
            }

            if (node["children"] is not JArray children)
                return;

            foreach (var child in children)
            {
                if (child is JObject childNode)
                    CollectImageFiles(childNode, referenced);
            }
        }

        static void NormalizeImageFileNames(JObject node)
        {
            if (node == null)
                return;

            if (node["fills"] is JArray fills)
            {
                foreach (var token in fills)
                {
                    if (token is not JObject fill)
                        continue;
                    var imageFile = fill.Value<string>("imageFile");
                    if (string.IsNullOrWhiteSpace(imageFile))
                        continue;
                    fill["imageFile"] = NormalizeFileName(imageFile);
                }
            }

            if (node["children"] is not JArray children)
                return;

            foreach (var child in children)
            {
                if (child is JObject childNode)
                    NormalizeImageFileNames(childNode);
            }
        }

        static string NormalizeFileName(string imageFile)
        {
            if (string.IsNullOrWhiteSpace(imageFile))
                return null;
            return Path.GetFileName(imageFile.Trim());
        }

        public static void WriteMetadata(
            JObject metadata,
            string exportDir,
            string artAssetRoot,
            BundleResult bundle)
        {
            if (metadata == null)
                return;

            metadata["assetMatchMode"] = "filename";
            metadata["assetFileNames"] = new JArray(bundle.AssetFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            metadata["artAssetConvention"] = new JObject
            {
                ["lookup"] = "filename",
                ["note"] = "Figma plugin: join(userAssetsFolder, fills.imageFile). Ignore assetDir/unityArtRoot."
            };

            if (bundle.MissingFiles.Count > 0)
                metadata["missingAssetFileNames"] = new JArray(bundle.MissingFiles);
        }
    }
}
