using Ludots.UI.Runtime;

namespace Ludots.UI.Compose;

public static class UiSceneComposer
{
    public static UiScene Compose(UiElementBuilder root, UiThemePack? theme = null, params UiStyleSheet[] styleSheets)
    {
        ArgumentNullException.ThrowIfNull(root);

        UiScene scene = new();
        int nextId = 1;
        scene.Mount(root.Build(scene.Dispatcher, ref nextId));
        if (styleSheets != null && styleSheets.Length > 0)
        {
            scene.SetStyleSheets(styleSheets);
        }

        if (theme != null)
        {
            scene.SetTheme(theme);
        }

        return scene;
    }
}
