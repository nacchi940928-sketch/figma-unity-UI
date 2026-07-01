using UnityEditor;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Ensures Figma UI textures import as Sprite (2D and UI).
    /// </summary>
    public static class UiSpriteImporter
    {
        public static bool Configure(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return false;

            var changed = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (!changed)
                return true;

            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        public static Sprite LoadSprite(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            assetPath = assetPath.Replace('\\', '/');
            Configure(assetPath);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
                return sprite;

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is Sprite subSprite)
                        return subSprite;
                }
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        public static bool IsUiAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var normalized = assetPath.Replace('\\', '/');
            return normalized.Contains("/UI/", System.StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Assets/UI/", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
