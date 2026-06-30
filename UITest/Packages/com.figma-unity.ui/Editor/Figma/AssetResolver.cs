using System.IO;
using UnityEditor;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class AssetResolver
    {
        public static string CopyImage(string exportDir, string imageFile, string screenName, string generatedRoot)
        {
            if (string.IsNullOrEmpty(imageFile))
                return null;

            var source = Path.Combine(exportDir, imageFile);
            if (!File.Exists(source))
                return null;

            var destDir = Path.Combine(generatedRoot, screenName).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(destDir))
            {
                EnsureFolder(generatedRoot);
                AssetDatabase.CreateFolder(generatedRoot, screenName);
            }

            var destAsset = Path.Combine(destDir, imageFile).Replace('\\', '/');
            var destFull = Path.GetFullPath(destAsset);
            Directory.CreateDirectory(Path.GetDirectoryName(destFull)!);
            File.Copy(source, destFull, true);
            AssetDatabase.ImportAsset(destAsset);
            return destAsset;
        }

        static void EnsureFolder(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var parts = assetPath.Split('/');
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
