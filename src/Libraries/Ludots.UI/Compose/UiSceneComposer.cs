using System;
using Ludots.UI.Runtime;

namespace Ludots.UI.Compose;

public static class UiSceneComposer
{
	public static UiScene Compose(UiElementBuilder root, UiThemePack? theme = null, params UiStyleSheet[] styleSheets)
	{
		ArgumentNullException.ThrowIfNull(root, "root");
		UiScene uiScene = new UiScene();
		int nextId = 1;
		uiScene.Mount(root.Build(uiScene.Dispatcher, ref nextId));
		if (styleSheets != null && styleSheets.Length != 0)
		{
			uiScene.SetStyleSheets(styleSheets);
		}
		if (theme != null)
		{
			uiScene.SetTheme(theme);
		}
		return uiScene;
	}
}
