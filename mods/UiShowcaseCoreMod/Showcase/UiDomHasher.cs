using System.Security.Cryptography;
using System.Text;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiDomHasher
{
    public static string Hash(UiScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Root == null ? string.Empty : Hash(scene.Root);
    }

    public static string Hash(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        StringBuilder builder = new();
        AppendNode(builder, node);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes)[..16];
    }

    private static void AppendNode(StringBuilder builder, UiNode node)
    {
        builder.Append(node.TagName)
            .Append('#').Append(node.ElementId)
            .Append('.')
            .Append(string.Join('.', node.ClassNames))
            .Append('[').Append(node.Kind).Append(']');

        foreach (UiNode child in node.Children)
        {
            builder.Append('{');
            AppendNode(builder, child);
            builder.Append('}');
        }
    }
}
