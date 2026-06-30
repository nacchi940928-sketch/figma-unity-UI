using System.Collections.Generic;
using FigmaUnity.UI.Editor.IR;

namespace FigmaUnity.UI.Editor.IR
{
    public static class IRValidator
    {
        public static List<string> Validate(IRNode root)
        {
            var warnings = new List<string>();
            if (root == null)
            {
                warnings.Add("Root node is null.");
                return warnings;
            }

            var ids = new HashSet<string>();
            Walk(root, ids, warnings);
            return warnings;
        }

        static void Walk(IRNode node, HashSet<string> ids, List<string> warnings)
        {
            if (string.IsNullOrEmpty(node.id))
                warnings.Add("Node missing id.");
            else if (!ids.Add(node.id))
                warnings.Add($"Duplicate id: {node.id}");

            if (string.IsNullOrEmpty(node.type))
                warnings.Add($"[{node.id}] missing type.");
            if (node.width <= 0 || node.height <= 0)
                warnings.Add($"[{node.id}] invalid size {node.width}x{node.height}.");

            if (node.children == null) return;
            foreach (var child in node.children)
                Walk(child, ids, warnings);
        }
    }
}
