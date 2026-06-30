using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Lossless round-trip between Figma export JObject ({ metadata, node }) and readable XML.
    /// Schema: figma-export / metadata[@*] / node[@*] with nested elements for objects and arrays.
    /// </summary>
    public static class FigmaDocumentXmlSerializer
    {
        public const string RootElementName = "figma-export";
        public const string NodeElementName = "node";
        public const string ChildrenElementName = "children";
        public const string ArrayItemElementName = "item";

        static readonly HashSet<string> MetadataChildKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "coordinateConvention",
            "exportProfile",
            "layoutAdjustments"
        };

        static readonly HashSet<string> ArrayElementNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "fills",
            "strokes",
            "effects",
            "dashPattern",
            "segments",
            "layoutGrids",
            "prunedIrIds"
        };

        public static JObject Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentException("XML is empty.");

            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, RootElementName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Root element must be <{RootElementName}>.");

            var result = new JObject();
            foreach (var child in root.Elements())
            {
                if (string.Equals(child.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase))
                    result["metadata"] = ReadMetadata(child);
                else if (string.Equals(child.Name.LocalName, NodeElementName, StringComparison.OrdinalIgnoreCase))
                    result["node"] = ReadNode(child);
            }

            if (result["node"] is not JObject)
                throw new ArgumentException("XML is missing <node>.");

            return result;
        }

        public static string Serialize(JObject root, bool indent = true)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var element = new XElement(RootElementName, new XAttribute("format", "v1"));
            if (root["metadata"] is JObject metadata)
                element.Add(WriteMetadata(metadata));

            if (root["node"] is JObject node)
                element.Add(WriteNode(node));

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), element);
            var settings = indent
                ? SaveOptions.None
                : SaveOptions.DisableFormatting;
            return doc.ToString(settings);
        }

        static XElement WriteMetadata(JObject metadata)
        {
            var element = new XElement("metadata");
            WriteScalarsAsAttributes(element, metadata);

            foreach (var prop in metadata.Properties())
            {
                if (prop.Value is JObject childObj && MetadataChildKeys.Contains(prop.Name))
                    element.Add(WriteObjectElement(prop.Name, childObj));

                if (prop.Value is JArray array && string.Equals(prop.Name, "prunedIrIds", StringComparison.Ordinal))
                    element.Add(WritePrimitiveArrayElement(prop.Name, array));
            }

            return element;
        }

        static XElement WriteNode(JObject node)
        {
            var element = new XElement(NodeElementName);
            WriteScalarsAsAttributes(element, node);

            foreach (var prop in node.Properties())
            {
                if (prop.Value.Type == JTokenType.Null)
                    continue;

                switch (prop.Name)
                {
                    case "children" when prop.Value is JArray children:
                        element.Add(WriteChildren(children));
                        break;
                    case "children":
                        break;
                    default:
                        if (prop.Value is JObject childObj)
                            element.Add(WriteObjectElement(prop.Name, childObj));
                        else if (prop.Value is JArray array)
                            element.Add(WriteArrayElement(prop.Name, array));
                        break;
                }
            }

            return element;
        }

        static XElement WriteChildren(JArray children)
        {
            var container = new XElement(ChildrenElementName);
            foreach (var token in children)
            {
                if (token is JObject child)
                    container.Add(WriteNode(child));
            }

            return container;
        }

        static XElement WriteObjectElement(string name, JObject obj)
        {
            var element = new XElement(SanitizeName(name));
            WriteScalarsAsAttributes(element, obj);

            foreach (var prop in obj.Properties())
            {
                if (prop.Value is JObject childObj)
                    element.Add(WriteObjectElement(prop.Name, childObj));
                else if (prop.Value is JArray array)
                    element.Add(WriteArrayElement(prop.Name, array));
            }

            return element;
        }

        static XElement WriteArrayElement(string name, JArray array)
        {
            var element = new XElement(SanitizeName(name));
            if (array.Count == 0)
            {
                element.SetAttributeValue("empty", "true");
                return element;
            }

            foreach (var token in array)
            {
                if (token is JObject obj)
                    element.Add(WriteObjectElement(ArrayItemElementName, obj));
                else
                    element.Add(new XElement(ArrayItemElementName, TokenToString(token)));
            }

            return element;
        }

        static XElement WritePrimitiveArrayElement(string name, JArray array)
        {
            var element = new XElement(SanitizeName(name));
            if (array.Count == 0)
            {
                element.SetAttributeValue("empty", "true");
                return element;
            }

            foreach (var token in array)
                element.Add(new XElement(ArrayItemElementName, TokenToString(token)));
            return element;
        }

        static JObject ReadMetadata(XElement element)
        {
            var metadata = ReadAttributesAsJObject(element);
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, "prunedIrIds", StringComparison.Ordinal))
                    metadata["prunedIrIds"] = IsEmptyArrayElement(child) ? new JArray() : ReadPrimitiveArray(child);
                else
                    metadata[child.Name.LocalName] = ReadObjectElement(child);
            }

            return metadata;
        }

        static JObject ReadNode(XElement element)
        {
            var node = ReadAttributesAsJObject(element);
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, ChildrenElementName, StringComparison.Ordinal))
                {
                    var children = new JArray();
                    foreach (var nodeChild in child.Elements())
                    {
                        if (string.Equals(nodeChild.Name.LocalName, NodeElementName, StringComparison.OrdinalIgnoreCase))
                            children.Add(ReadNode(nodeChild));
                    }

                    node["children"] = children;
                    continue;
                }

                if (ArrayElementNames.Contains(child.Name.LocalName))
                {
                    node[child.Name.LocalName] = IsEmptyArrayElement(child)
                        ? new JArray()
                        : ReadArrayElement(child);
                    continue;
                }

                if (child.Elements().Any())
                    node[child.Name.LocalName] = ReadArrayElement(child);
                else if (child.HasAttributes)
                    node[child.Name.LocalName] = ReadObjectElement(child);
                else
                    node[child.Name.LocalName] = new JObject();
            }

            return node;
        }

        static JObject ReadObjectElement(XElement element)
        {
            var obj = ReadAttributesAsJObject(element);
            foreach (var child in element.Elements())
            {
                if (child.Elements().Any())
                    obj[child.Name.LocalName] = ReadArrayElement(child);
                else
                    obj[child.Name.LocalName] = ReadObjectElement(child);
            }

            return obj;
        }

        static bool IsEmptyArrayElement(XElement element)
        {
            return string.Equals(element.Attribute("empty")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                || (!element.HasAttributes && !element.HasElements);
        }

        static JArray ReadArrayElement(XElement element)
        {
            var array = new JArray();
            foreach (var item in element.Elements())
            {
                if (item.HasAttributes || item.HasElements)
                    array.Add(ReadObjectElement(item));
                else
                    array.Add(ParseToken(item.Value));
            }

            return array;
        }

        static JArray ReadPrimitiveArray(XElement element)
        {
            var array = new JArray();
            foreach (var item in element.Elements())
                array.Add(ParseToken(item.Value));
            return array;
        }

        static JObject ReadAttributesAsJObject(XElement element)
        {
            var obj = new JObject();
            foreach (var attr in element.Attributes())
            {
                if (string.Equals(attr.Name.LocalName, "xmlns", StringComparison.OrdinalIgnoreCase))
                    continue;
                obj[attr.Name.LocalName] = ParseToken(attr.Value);
            }

            return obj;
        }

        static void WriteScalarsAsAttributes(XElement element, JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (prop.Value is not JValue value)
                    continue;

                if (value.Type == JTokenType.Null)
                    continue;

                element.SetAttributeValue(SanitizeName(prop.Name), TokenToString(value));
            }
        }

        static string TokenToString(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
                JTokenType.Float or JTokenType.Integer => Convert.ToString(
                    token.Value<object>(),
                    CultureInfo.InvariantCulture),
                JTokenType.String => token.Value<string>(),
                JTokenType.Null => string.Empty,
                _ => token.ToString(Newtonsoft.Json.Formatting.None)
            };
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

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var builder = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    builder.Append(ch);
                else
                    builder.Append('_');
            }

            return builder.ToString();
        }
    }
}
