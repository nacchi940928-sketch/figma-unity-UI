namespace FigmaUnity.UI.Editor.Figma
{
    public class FigmaImportSettings
    {
        public string GeneratedRoot = "Assets/UI/Generated";
        /// <summary>Unity project folder for art textures; matched by fills.imageFile file name.</summary>
        public string ArtAssetRoot = ArtAssetResolver.DefaultArtRoot;
        public bool UseRootNormalized = true;
        /// <summary>Validate 预览时不复制 png，仅 Import 时复制。</summary>
        public bool CopyAssets = true;
    }
}
