using System;
using System.IO;
using System.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class FigmaExportPackage
    {
        public static bool TryLocate(
            string dir,
            out string documentPath,
            out string screenName,
            FigmaDocumentFormat format = FigmaDocumentFormat.Auto)
        {
            documentPath = null;
            screenName = null;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;

            var xmlFiles = Directory.GetFiles(dir, "*-full.xml")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var jsonFiles = Directory.GetFiles(dir, "*-full.json")
                .Where(f => !Path.GetFileName(f).Contains("-unity-export"))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            switch (format)
            {
                case FigmaDocumentFormat.Xml:
                    if (xmlFiles.Length == 0)
                        return false;
                    documentPath = xmlFiles[0];
                    break;
                case FigmaDocumentFormat.Json:
                    if (jsonFiles.Length == 0)
                        return false;
                    documentPath = jsonFiles[0];
                    break;
                default:
                    if (xmlFiles.Length > 0)
                        documentPath = xmlFiles[0];
                    else if (jsonFiles.Length > 0)
                        documentPath = jsonFiles[0];
                    else
                        return false;
                    break;
            }

            screenName = GetScreenNameFromDocumentPath(documentPath);
            return true;
        }

        public static string[] ListFullDocuments(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Array.Empty<string>();

            return Directory.GetFiles(dir, "*-full.*")
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return name.EndsWith("-full.json", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("-full.xml", StringComparison.OrdinalIgnoreCase);
                })
                .Where(f => !Path.GetFileName(f).Contains("-unity-export"))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToArray();
        }

        public static string GetScreenNameFromDocumentPath(string documentPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(documentPath);
            if (fileName.EndsWith("-full", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - 5);
            if (fileName.EndsWith("-unity-export", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - 13);
            return fileName;
        }

        public static bool IsSupportedDocumentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetAssetDirectory(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath))
                return null;
            return Path.GetDirectoryName(Path.GetFullPath(documentPath));
        }

        public static string ResolveUnityExportPath(
            string dir,
            string screenName,
            string templatePath,
            FigmaDocumentFormat outputFormat,
            string userPath = null)
        {
            var extension = outputFormat == FigmaDocumentFormat.Xml ? ".xml" : ".json";
            var templateFull = Path.GetFullPath(templatePath);

            if (!string.IsNullOrWhiteSpace(userPath))
            {
                var userFull = Path.GetFullPath(userPath);
                if (!string.Equals(userFull, templateFull, System.StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(userFull))
                    return userFull;
            }

            var first = Path.GetFullPath(Path.Combine(dir, $"{screenName}-unity-export{extension}"));
            if (!string.Equals(first, templateFull, System.StringComparison.OrdinalIgnoreCase) && !File.Exists(first))
                return first;

            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, $"{screenName}-unity-export-{i}{extension}"));
                if (!string.Equals(candidate, templateFull, System.StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(candidate))
                    return candidate;
            }

            return first;
        }

        public static string[] FindMissingImageFiles(FigmaNode root, string dir)
        {
            var missing = new System.Collections.Generic.List<string>();
            if (root == null) return missing.ToArray();
            Walk(root, dir, missing);
            return missing.ToArray();
        }

        static void Walk(FigmaNode node, string dir, System.Collections.Generic.List<string> missing)
        {
            if (node.fills != null)
            {
                foreach (var fill in node.fills)
                {
                    if (fill?.type == "IMAGE" && !string.IsNullOrEmpty(fill.imageFile))
                    {
                        var path = Path.Combine(dir, fill.imageFile);
                        if (!File.Exists(path))
                            missing.Add(fill.imageFile);
                    }
                }
            }

            if (node.children == null) return;
            foreach (var child in node.children)
                Walk(child, dir, missing);
        }
    }
}
