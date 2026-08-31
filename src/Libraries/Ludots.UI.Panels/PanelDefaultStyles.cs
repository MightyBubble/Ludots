using System;
using System.IO;
using System.Reflection;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace Ludots.UI.Panels;

internal static class PanelDefaultStyles
{
    private const string ResourceName = "Ludots.UI.Panels.Assets.default-controls.css";

    internal static UiStyleSheet Load()
    {
        Assembly assembly = typeof(PanelDefaultStyles).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Panel default stylesheet resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return UiCssParser.ParseStyleSheet(reader.ReadToEnd());
    }
}
