using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Parses Figma Tool 1 "design-export" XML (element-per-field, children as &lt;child&gt;).
    /// Converts to the same JObject shape as *-full.json for the existing import pipeline.
    /// </summary>
    public static class FigmaDesignExportXmlSerializer
    {
        public const string RootElementName = "design-export";

        static readonly Dictionary<string, string> ArrayItemTags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["children"] = "child",
            ["fills"] = "fill",
            ["strokes"] = "stroke",
            ["effects"] = "effect",
            ["segments"] = "segment",
            ["stops"] = "stop",
            ["layoutGrids"] = "layoutGrid",
            ["dashPattern"] = "item"
        };

        public static bool CanParse(XDocument doc)
        {
            return string.Equals(doc.Root?.Name.LocalName, RootElementName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanParse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return false;

            var trimmed = xml.TrimStart();
            return trimmed.StartsWith($"<{RootElementName}", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains($"<{RootElementName}", StringComparison.Ordinal);
        }

        public static JObject Parse(string xml)
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            if (!CanParse(doc))
                throw new ArgumentException($"Root element must be <{RootElementName}>.");

            var root = doc.Root;
            var result = new JObject();

            var metadataEl = root.Element("metadata");
            if (metadataEl != null)
                result["metadata"] = ReadMetadata(metadataEl);

            var nodeEl = root.Element("node");
            if (nodeEl == null)
                throw new ArgumentException("XML is missing <node>.");

            result["node"] = ReadNode(nodeEl);
            return result;
        }

        static JObject ReadMetadata(XElement metadata)
        {
            var obj = new JObject();
            foreach (var child in metadata.Elements())
            {
                if (!child.HasElements)
                    obj[child.Name.LocalName] = ParseToken(child.Value);
            }

            return obj;
        }

        static JObject ReadNode(XElement element)
        {
            return ReadStructuredObject(element);
        }

        static JObject ReadStructuredObject(XElement element)
        {
            var obj = new JObject();
            foreach (var child in element.Elements())
            {
                var name = child.Name.LocalName;
                if (string.Equals(name, "children", StringComparison.Ordinal))
                {
                    obj["children"] = ReadChildrenArray(child);
                    continue;
                }

                if (ArrayItemTags.ContainsKey(name))
                {
                    obj[name] = ReadNamedArray(child, name);
                    continue;
                }

                if (!child.HasElements)
                {
                    obj[name] = ParseToken(child.Value);
                    continue;
                }

                if (child.Elements().All(e => !e.HasElements))
                    obj[name] = ReadFlatObject(child);
                else
                    obj[name] = ReadStructuredObject(child);
            }

            return obj;
        }

        static JObject ReadFlatObject(XElement element)
        {
            var obj = new JObject();
            foreach (var child in element.Elements())
                obj[child.Name.LocalName] = ParseToken(child.Value);
            return obj;
        }

        static JArray ReadChildrenArray(XElement children)
        {
            var array = new JArray();
            if (!children.HasElements)
                return array;

            foreach (var child in children.Elements("child"))
                array.Add(ReadNode(child));

            return array;
        }

        static JArray ReadNamedArray(XElement container, string containerName)
        {
            if (!container.HasElements)
                return new JArray();

            if (!ArrayItemTags.TryGetValue(containerName, out var itemTag))
                itemTag = "item";

            var array = new JArray();
            foreach (var item in container.Elements(itemTag))
            {
                if (!item.HasElements)
                    array.Add(ParseToken(item.Value));
                else if (item.Elements().All(e => !e.HasElements))
                    array.Add(ReadFlatObject(item));
                else
                    array.Add(ReadStructuredObject(item));
            }

            return array;
        }

        static JToken ParseToken(string raw)
        {
            if (raw == null)
                return JValue.CreateNull();

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return new JValue(true);
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return new JValue(false);

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                return new JValue(longValue);

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                return new JValue(doubleValue);

            return new JValue(raw);
        }
    }
}
