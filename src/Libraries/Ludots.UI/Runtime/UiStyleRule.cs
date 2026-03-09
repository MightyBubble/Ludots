using System;

namespace Ludots.UI.Runtime;

public sealed class UiStyleRule
{
	public UiSelector Selector { get; }

	public UiStyleDeclaration Declaration { get; }

	public int Order { get; }

	public UiStyleRule(UiSelector selector, UiStyleDeclaration declaration, int order)
	{
		Selector = selector ?? throw new ArgumentNullException("selector");
		Declaration = declaration ?? throw new ArgumentNullException("declaration");
		Order = order;
	}
}
