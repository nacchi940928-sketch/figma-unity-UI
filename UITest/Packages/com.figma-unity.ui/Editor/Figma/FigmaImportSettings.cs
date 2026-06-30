namespace FigmaUnity.UI.Editor.Figma
{
    public class FigmaImportSettings
    {
        public string GeneratedRoot = "Assets/UI/Generated";
        public bool UseRootNormalized = true;
        /// <summary>Validate 预览时不复制 png，仅 Import 时复制。</summary>
        public bool CopyAssets = true;
    }
}
