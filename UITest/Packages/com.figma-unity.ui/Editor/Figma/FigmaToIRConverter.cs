using System.IO;
using FigmaUnity.UI.Editor.IR;
using Newtonsoft.Json;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class FigmaToIRConverter
    {
        public static IRNode Convert(
            string exportDir,
            FigmaImportSettings settings,
            FigmaDocumentFormat format = FigmaDocumentFormat.Auto)
        {
            if (!FigmaExportPackage.TryLocate(exportDir, out var documentPath, out var screenName, format))
                throw new FileNotFoundException(
                    "No *-full.xml or *-full.json found in export directory.",
                    exportDir);

            return ConvertFile(documentPath, settings, exportDir, screenName);
        }

        public static IRNode ConvertFile(
            string documentPath,
            FigmaImportSettings settings,
            string exportDir = null,
            string screenName = null)
        {
            exportDir ??= Path.GetDirectoryName(documentPath);
            screenName ??= FigmaExportPackage.GetScreenNameFromDocumentPath(documentPath);

            var doc = FigmaDocumentSerializer.LoadDocument(documentPath);
            if (doc?.node == null)
                throw new JsonException("Invalid Figma export: missing node.");

            var copiedAssets = new System.Collections.Generic.Dictionary<string, string>();
            var root = FigmaV2NodeMapper.MapNode(doc.node, null, true, settings, exportDir, screenName, copiedAssets);
            if (doc.metadata != null)
            {
                root.meta["screenName"] = doc.metadata.componentName;
                root.meta["exportedAt"] = doc.metadata.exportedAt;
                root.meta["figmaExporter"] = doc.metadata.plugin;
                root.meta["sourceFormat"] = FigmaDocumentSerializer.IsXmlPath(documentPath) ? "xml" : "json";
            }

            return root;
        }
    }
}
