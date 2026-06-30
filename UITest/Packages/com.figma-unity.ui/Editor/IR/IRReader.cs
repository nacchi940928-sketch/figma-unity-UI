using System.IO;
using FigmaUnity.UI.Editor.Figma;

namespace FigmaUnity.UI.Editor.IR
{
    public static class IRReader
    {
        public static IRNode ReadFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("IR JSON not found.", path);

            var json = File.ReadAllText(path);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<IRNode>(json);
        }

        public static IRNode ReadFromFigmaExport(string exportDir, FigmaImportSettings settings = null)
        {
            return FigmaToIRConverter.Convert(exportDir, settings ?? new FigmaImportSettings());
        }
    }
}
