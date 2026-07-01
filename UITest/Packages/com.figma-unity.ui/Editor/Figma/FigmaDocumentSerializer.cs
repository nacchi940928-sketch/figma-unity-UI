using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    public enum FigmaDocumentFormat
    {
        Auto,
        Json,
        Xml
    }

    public static class FigmaDocumentSerializer
    {
        static readonly JsonSerializer DocumentDeserializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters =
            {
                new FlexibleFloatConverter(),
                new FlexibleIntConverter()
            }
        });

        public static bool IsXmlPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsJsonPath(string path)
        {
            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(ext);
        }

        public static FigmaDocumentFormat DetectFormat(string path)
        {
            return IsXmlPath(path) ? FigmaDocumentFormat.Xml : FigmaDocumentFormat.Json;
        }

        public static JObject Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("Figma document not found.", path);

            var text = File.ReadAllText(path);
            if (!IsXmlPath(path))
                return JObject.Parse(text);

            if (FigmaDesignExportXmlSerializer.CanParse(text))
                return FigmaDesignExportXmlSerializer.Parse(text);

            return FigmaDocumentXmlSerializer.Parse(text);
        }

        public static void Save(JObject root, string path, bool indent = true)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Output path is required.", nameof(path));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (IsXmlPath(path))
            {
                File.WriteAllText(path, FigmaDocumentXmlSerializer.Serialize(root, indent), System.Text.Encoding.UTF8);
                return;
            }

            File.WriteAllText(path, root.ToString(indent ? Formatting.Indented : Formatting.None));
        }

        public static FigmaExportDocument LoadDocument(string path)
        {
            var root = Load(path);
            return root.ToObject<FigmaExportDocument>(DocumentDeserializer)
                ?? throw new JsonException("Invalid Figma export document.");
        }

        public static JsonSerializer GetDocumentDeserializer() => DocumentDeserializer;

        public static void SaveDocument(JObject root, string path, bool indent = true)
        {
            Save(root, path, indent);
        }
    }
}
