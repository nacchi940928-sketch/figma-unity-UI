using System.IO;
using FigmaUnity.UI.Editor.Figma;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FigmaUnity.UI.Editor.Export
{
    public static class FigmaExportWriter
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        public static void Write(FigmaExportDocument document, string outputPath)
        {
            var json = JsonConvert.SerializeObject(document, Settings);
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(outputPath, json);
        }
    }
}
