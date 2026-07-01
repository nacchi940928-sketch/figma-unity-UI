using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;

namespace FigmaUnity.UI.Editor.Figma
{
    public static class FigmaV2NodeMapper
    {
        public static IRNode MapNode(
            FigmaNode node,
            FigmaNode parent,
            bool isRoot,
            FigmaImportSettings settings,
            string exportDir,
            string screenName,
            Dictionary<string, string> copiedAssets)
        {
            var irType = TypeMapper.MapIrType(node);
            var (x, y) = ResolvePosition(node, parent, isRoot, settings);
            var ir = new IRNode
            {
                version = "1.0.0",
                id = node.irId,
                type = irType,
                x = x,
                y = y,
                width = node.width,
                height = node.height,
                scaleX = node.scaleX,
                scaleY = node.scaleY,
                anchor = "top-left",
                opacity = node.opacity,
                cornerRadius = node.cornerRadius,
                rotation = node.rotation,
                constraints = StyleMapper.MapConstraints(node),
                layoutSizingHorizontal = node.layoutSizingHorizontal,
                layoutSizingVertical = node.layoutSizingVertical,
                fills = StyleMapper.MapFills(node, irType),
                stroke = StyleMapper.MapStroke(node),
                layout = StyleMapper.MapLayout(node.layout),
                text = StyleMapper.MapText(node),
                meta = BuildMeta(node, parent, isRoot, settings, exportDir, screenName, copiedAssets, irType)
            };

            if (node.type == "LINE")
            {
                ir.height = node.strokeWeight > 0 ? node.strokeWeight : 1f;
            }

            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    ir.children.Add(MapNode(child, node, false, settings, exportDir, screenName, copiedAssets));
                }
            }

            return ir;
        }

        /// <summary>
        /// Prefer rootX/rootY delta so GROUP children with absolute x/y still land correctly.
        /// </summary>
        static (float x, float y) ResolvePosition(
            FigmaNode node,
            FigmaNode parent,
            bool isRoot,
            FigmaImportSettings settings)
        {
            if (isRoot && settings.UseRootNormalized)
                return (0f, 0f);

            if (parent != null)
                return (node.rootX - parent.rootX, node.rootY - parent.rootY);

            return (node.x, node.y);
        }

        static Dictionary<string, object> BuildMeta(
            FigmaNode node,
            FigmaNode parent,
            bool isRoot,
            FigmaImportSettings settings,
            string exportDir,
            string screenName,
            Dictionary<string, string> copiedAssets,
            string irType)
        {
            var meta = new Dictionary<string, object>
            {
                ["figmaNodeId"] = node.id,
                ["figmaName"] = node.name,
                ["figmaType"] = node.type,
                ["visible"] = node.visible,
                ["rootX"] = node.rootX,
                ["rootY"] = node.rootY
            };

            if (node.fontWeight > 0)
                meta["fontWeight"] = node.fontWeight;
            else if (node.type == "TEXT")
                meta["fontWeight"] = StyleMapper.ResolveFontWeight(node);

            var shadow = StyleMapper.MapShadowMeta(node);
            if (shadow != null)
                meta["shadow"] = shadow;

            if (TypeMapper.HasImageFill(node) && node.fills != null)
            {
                foreach (var fill in node.fills)
                {
                    if (fill?.type != "IMAGE" || string.IsNullOrEmpty(fill.imageFile))
                        continue;

                    meta["imageFile"] = fill.imageFile;
                    meta["imageHash"] = fill.imageHash;
                    var assetPath = ArtAssetResolver.ResolveUnityAssetPath(
                        fill.imageFile,
                        settings.ArtAssetRoot,
                        screenName,
                        exportDir,
                        settings.GeneratedRoot,
                        settings.CopyAssets);
                    if (!string.IsNullOrEmpty(assetPath))
                        copiedAssets[fill.imageFile] = assetPath;
                    else if (!settings.CopyAssets)
                    {
                        var sourcePath = System.IO.Path.Combine(exportDir ?? string.Empty, fill.imageFile);
                        if (System.IO.File.Exists(sourcePath))
                            meta["sourceImagePath"] = sourcePath;
                    }
                    if (!string.IsNullOrEmpty(assetPath))
                        meta["assetPath"] = assetPath;
                    break;
                }
            }

            if (node.type == "VECTOR")
            {
                meta["assetPath"] = string.Empty;
                if (parent != null && !string.IsNullOrEmpty(parent.name))
                    meta["iconHint"] = parent.name;
            }

            return meta;
        }
    }
}
