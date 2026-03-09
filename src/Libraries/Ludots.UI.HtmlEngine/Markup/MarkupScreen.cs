using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class MarkupScreen<TCodeBehind> where TCodeBehind : class
{
	public UiScene Scene { get; }

	public TCodeBehind CodeBehind { get; }

	private MarkupScreen(UiScene scene, TCodeBehind codeBehind)
	{
		Scene = scene;
		CodeBehind = codeBehind;
	}

	public static MarkupScreen<TCodeBehind> Create(string html, string css, TCodeBehind codeBehind, UiThemePack? theme = null)
	{
		UiMarkupLoader uiMarkupLoader = new UiMarkupLoader();
		UiScene scene = uiMarkupLoader.LoadScene(html, css, codeBehind, theme);
		return new MarkupScreen<TCodeBehind>(scene, codeBehind);
	}
}
